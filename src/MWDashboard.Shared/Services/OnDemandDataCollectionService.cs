using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public class OnDemandDataCollectionService : IDataCollectionService
{
    private readonly IGraphReportService _graphService;
    private readonly IMauDataService _dataService;
    private readonly ILogger<OnDemandDataCollectionService> _logger;
    private readonly int _maxConcurrency;

    // AIMD adaptive-concurrency tuning. On an observed Graph throttle the effective concurrency is
    // multiplicatively decreased; after a clean window it additively recovers toward the cap.
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GatePollInterval = TimeSpan.FromMilliseconds(50);

    public OnDemandDataCollectionService(
        IGraphReportService graphService,
        IMauDataService dataService,
        ILogger<OnDemandDataCollectionService> logger,
        IConfiguration? configuration = null)
    {
        _graphService = graphService;
        _dataService = dataService;
        _logger = logger;
        // Independent Graph metrics for a single tenant are collected concurrently with this bounded
        // degree of parallelism. It is the *ceiling*; the adaptive gate only ever moves below it in
        // response to live throttle signals. Graph throttles per-app/per-tenant, so the cap keeps the
        // collection window short without provoking sustained 429s. Configurable; conservative default.
        _maxConcurrency = Math.Max(1, configuration?.GetValue("Collection:MaxParallelMetricsPerTenant", 6) ?? 6);
    }

    /// <summary>
    /// Runs the supplied collection steps with an adaptive, bounded degree of concurrency. Each step
    /// is a self-contained Graph GET + DB save; a failure in one step is logged and does not abort the
    /// others (every metric is independent), so a single feature's error never costs the rest.
    ///
    /// Concurrency adapts in real time to the tenant's live <see cref="GraphThrottleSignal"/> (fed by
    /// the HTTP middleware): the effective permit count halves on a recent throttle (AIMD multiplicative
    /// decrease) and recovers by one after a clean window (additive increase), and any server-supplied
    /// Retry-After is honored before the next request is issued. With no throttling it simply runs at
    /// the configured cap, identical to a fixed semaphore and with negligible overhead.
    /// </summary>
    private async Task RunStepsAsync(
        string tenantName,
        GraphThrottleSignal signal,
        IReadOnlyList<(string Name, Func<Task> Action)> steps,
        CancellationToken ct)
    {
        const int min = 1;
        var max = _maxConcurrency;
        var target = max;     // current allowed concurrency (starts optimistic at the cap)
        var inFlight = 0;
        var lastTarget = max; // for change logging
        using var gate = new SemaphoreSlim(1, 1); // guards target/inFlight bookkeeping

        async Task AcquireAsync()
        {
            while (true)
            {
                // 1. Honor any server-mandated back-off before competing for a slot.
                var wait = signal.RetryAfterDelay();
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct);

                await gate.WaitAsync(ct);
                try
                {
                    // 2. AIMD: shrink if recently throttled, otherwise recover toward the cap.
                    if (signal.ThrottledWithin(ThrottleWindow))
                        target = Math.Max(min, target / 2);
                    else if (target < max)
                        target = Math.Min(max, target + 1);

                    if (target != lastTarget)
                    {
                        _logger.LogInformation(
                            "Adaptive collection concurrency for tenant {TenantName}: {Old} -> {New} (cap {Max})",
                            tenantName, lastTarget, target, max);
                        lastTarget = target;
                    }

                    if (inFlight < target)
                    {
                        inFlight++;
                        return;
                    }
                }
                finally
                {
                    gate.Release();
                }

                // Gate is full at the current target — yield briefly and re-evaluate.
                await Task.Delay(GatePollInterval, ct);
            }
        }

        async Task ReleaseAsync()
        {
            await gate.WaitAsync(ct);
            try { inFlight--; }
            finally { gate.Release(); }
        }

        var tasks = steps.Select(async step =>
        {
            await AcquireAsync();
            try
            {
                await step.Action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Collection step '{Step}' failed for tenant {TenantName}", step.Name, tenantName);
            }
            finally
            {
                await ReleaseAsync();
            }
        });
        await Task.WhenAll(tasks);
    }


    public async Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default)
    {
        _logger.LogInformation("On-demand data collection for tenant {TenantName} ({TenantId})", tenantName, tenantId);

        // --- Phase 0: licenses first. The SKUs drive tier gating (below), license-assignment
        // analysis and the consumption score, so this one call must complete before the rest. ---
        var licenses = await _graphService.GetSubscribedSkusAsync(tenantId);
        if (licenses.Count > 0)
            await _dataService.SaveLicensesAsync(licenses);

        // Tenant licensing tiers, derived from the SKUs (no extra Graph call). Sign-in-based
        // features (signInActivity, sign-in logs, Identity Protection) require Entra P1/P2; the
        // threat-protection features require Defender for Office 365 P1/P2 — gated features are
        // simply not queued instead of issuing a call that always returns 403.
        var entraTier = TenantEntraTier.FromLicenses(tenantId, tenantName, licenses.Select(l => l.SkuPartNumber));
        var defenderTier = TenantDefenderTier.FromLicenses(tenantId, tenantName, licenses.Select(l => l.SkuPartNumber));

        // Results captured for the consumption score (assigned inside their collection steps below).
        List<StorageSnapshot> storage = [];
        List<WorkloadActivitySnapshot> activities = [];
        List<UserSegmentSnapshot> segments = [];

        // --- Phase 1: independent metrics. Each step is a self-contained Graph GET + DB save with
        // no ordering dependency on the others, so they run concurrently (bounded). ---
        var steps = new List<(string Name, Func<Task> Action)>
        {
            ("ActiveUserCounts", async () =>
            {
                var snapshots = await _graphService.GetActiveUserCountsAsync(tenantId);
                if (snapshots.Count > 0)
                {
                    foreach (var s in snapshots) s.TenantName = tenantName;
                    await _dataService.SaveSnapshotsAsync(snapshots);
                }
            }),
            ("Subscriptions", async () =>
            {
                var subscriptions = await _graphService.GetDirectorySubscriptionsAsync(tenantId);
                if (subscriptions.Count > 0)
                {
                    foreach (var s in subscriptions) s.TenantName = tenantName;
                    await _dataService.SaveSubscriptionsAsync(tenantId, DateTime.UtcNow.Date, subscriptions);
                }
            }),
            ("MessageCenter", async () =>
            {
                var posts = await _graphService.GetMessageCenterPostsAsync(tenantId);
                if (posts.Count > 0)
                    await _dataService.SaveMessageCenterPostsAsync(posts);
            }),
            ("WorkloadActivity", async () =>
            {
                activities = await _graphService.GetWorkloadActivityAsync(tenantId);
                if (activities.Count > 0)
                {
                    foreach (var a in activities) a.TenantName = tenantName;
                    await _dataService.SaveWorkloadActivityAsync(activities);
                }
            }),
            ("CopilotUsage", async () =>
            {
                var copilot = await _graphService.GetCopilotUsageAsync(tenantId);
                if (copilot.Count > 0)
                {
                    foreach (var c in copilot) c.TenantName = tenantName;
                    await _dataService.SaveCopilotUsageAsync(copilot);
                }
            }),
            ("UserSegmentation", async () =>
            {
                segments = await _graphService.GetUserSegmentationAsync(tenantId);
                if (segments.Count > 0)
                {
                    foreach (var s in segments) s.TenantName = tenantName;
                    await _dataService.SaveUserSegmentsAsync(segments);
                }
            }),
            ("DepartmentUsage", async () =>
            {
                var depts = await _graphService.GetDepartmentUsageAsync(tenantId);
                if (depts.Count > 0)
                {
                    foreach (var d in depts) d.TenantName = tenantName;
                    await _dataService.SaveDepartmentUsageAsync(depts);
                }
            }),
            ("StorageUsage", async () =>
            {
                storage = await _graphService.GetStorageUsageAsync(tenantId);
                if (storage.Count > 0)
                {
                    foreach (var s in storage) s.TenantName = tenantName;
                    await _dataService.SaveStorageAsync(storage);
                }
            }),
            ("M365AppUsage", async () =>
            {
                var appUsage = await _graphService.GetM365AppUsageAsync(tenantId);
                if (appUsage.Count > 0)
                {
                    foreach (var a in appUsage) a.TenantName = tenantName;
                    await _dataService.SaveM365AppUsageAsync(appUsage);
                }
            }),
            ("M365AppUserDetail", async () =>
            {
                var appUserDetail = await _graphService.GetM365AppUserDetailAsync(tenantId);
                if (appUserDetail.Count > 0)
                {
                    foreach (var a in appUserDetail) a.TenantName = tenantName;
                    await _dataService.SaveM365AppUserDetailAsync(appUserDetail);
                }
            }),
            ("Office365Activations", async () =>
            {
                var (activationCounts, activationUsers) = await _graphService.GetOffice365ActivationsAsync(tenantId);
                if (activationCounts.Count > 0)
                {
                    foreach (var a in activationCounts) a.TenantName = tenantName;
                    await _dataService.SaveOffice365ActivationsAsync(activationCounts);
                }
                if (activationUsers.Count > 0)
                {
                    foreach (var a in activationUsers) a.TenantName = tenantName;
                    await _dataService.SaveOffice365ActivationUsersAsync(activationUsers);
                }
            }),
            ("SecureScore", async () =>
            {
                var (secureScores, secureControls) = await _graphService.GetSecureScoreAsync(tenantId);
                if (secureScores.Count > 0)
                {
                    foreach (var s in secureScores) s.TenantName = tenantName;
                    await _dataService.SaveSecureScoresAsync(secureScores);
                }
                if (secureControls.Count > 0)
                {
                    foreach (var c in secureControls) c.TenantName = tenantName;
                    await _dataService.SaveSecureScoreControlsAsync(secureControls);
                }
            }),
            ("MfaRegistration", async () =>
            {
                var mfa = await _graphService.GetMfaRegistrationAsync(tenantId);
                if (mfa != null)
                {
                    mfa.TenantName = tenantName;
                    await _dataService.SaveMfaRegistrationAsync(mfa);
                }
            }),
            ("ServiceHealth", async () =>
            {
                var (healthServices, healthIssues) = await _graphService.GetServiceHealthAsync(tenantId);
                if (healthServices.Count > 0)
                {
                    foreach (var s in healthServices) s.TenantName = tenantName;
                    await _dataService.SaveServiceHealthAsync(healthServices);
                }
                foreach (var i in healthIssues) i.TenantName = tenantName;
                await _dataService.SaveServiceHealthIssuesAsync(healthIssues);
            }),
            ("DeviceCompliance", async () =>
            {
                var (deviceCompliance, devicePatch) = await _graphService.GetDeviceComplianceAsync(tenantId);
                if (deviceCompliance != null)
                {
                    deviceCompliance.TenantName = tenantName;
                    await _dataService.SaveDeviceComplianceAsync(deviceCompliance);
                }
                if (devicePatch.Count > 0)
                {
                    foreach (var p in devicePatch) p.TenantName = tenantName;
                    await _dataService.SaveDevicePatchAsync(tenantId, devicePatch[0].ReportDate, devicePatch);
                }
            }),
            ("StaleDevices", async () =>
            {
                var staleDevices = await _graphService.GetStaleDevicesAsync(tenantId);
                if (staleDevices.Count > 0)
                {
                    foreach (var d in staleDevices) d.TenantName = tenantName;
                    await _dataService.SaveStaleDevicesAsync(tenantId, staleDevices[0].ReportDate, staleDevices);
                }
            }),
            ("ConditionalAccess", async () =>
            {
                var conditionalAccess = await _graphService.GetConditionalAccessAsync(tenantId);
                if (conditionalAccess != null)
                {
                    conditionalAccess.TenantName = tenantName;
                    await _dataService.SaveConditionalAccessAsync(conditionalAccess);
                }
            }),
            ("GuestUsers", async () =>
            {
                var guests = await _graphService.GetGuestUsersAsync(tenantId);
                if (guests != null)
                {
                    guests.TenantName = tenantName;
                    await _dataService.SaveGuestUsersAsync(guests);
                }
            }),
            ("MailboxUsage", async () =>
            {
                var (mailboxAgg, topMailboxes) = await _graphService.GetMailboxUsageAsync(tenantId);
                if (mailboxAgg != null)
                {
                    mailboxAgg.TenantName = tenantName;
                    await _dataService.SaveMailboxUsageAsync(mailboxAgg);
                }
                if (topMailboxes.Count > 0)
                {
                    foreach (var m in topMailboxes) m.TenantName = tenantName;
                    await _dataService.SaveTopMailboxesAsync(topMailboxes);
                }
            }),
            ("TeamsDeviceUsage", async () =>
            {
                var teamsDevices = await _graphService.GetTeamsDeviceUsageAsync(tenantId);
                if (teamsDevices != null)
                {
                    teamsDevices.TenantName = tenantName;
                    await _dataService.SaveTeamsDeviceUsageAsync(teamsDevices);
                }
            }),
            ("TeamsTeamActivity", async () =>
            {
                var teamsActivity = await _graphService.GetTeamsTeamActivityAsync(tenantId);
                if (teamsActivity.Count > 0)
                {
                    foreach (var t in teamsActivity) t.TenantName = tenantName;
                    await _dataService.SaveTeamsTeamActivityAsync(tenantId, DateTime.UtcNow.Date, teamsActivity);
                }
            }),
            ("SiteUsage", async () =>
            {
                var (siteAggs, siteDetails) = await _graphService.GetSiteUsageAsync(tenantId);
                if (siteAggs.Count > 0)
                {
                    foreach (var s in siteAggs) s.TenantName = tenantName;
                    await _dataService.SaveSiteUsageAsync(siteAggs);
                }
                if (siteDetails.Count > 0)
                {
                    foreach (var s in siteDetails) s.TenantName = tenantName;
                    await _dataService.SaveSiteUsageDetailAsync(siteDetails);
                }
            }),
            ("YammerActivity", async () =>
            {
                var yammer = await _graphService.GetYammerActivityAsync(tenantId);
                if (yammer != null)
                {
                    yammer.TenantName = tenantName;
                    await _dataService.SaveYammerActivityAsync(yammer);
                }
            }),
            ("GroupSprawl", async () =>
            {
                var groups = await _graphService.GetGroupSprawlAsync(tenantId);
                if (groups != null)
                {
                    groups.TenantName = tenantName;
                    await _dataService.SaveGroupSprawlAsync(groups);
                }
            }),
            ("AppCredentials", async () =>
            {
                var appCredentials = await _graphService.GetAppCredentialsAsync(tenantId);
                if (appCredentials.Count > 0)
                {
                    foreach (var c in appCredentials) c.TenantName = tenantName;
                    await _dataService.SaveAppCredentialsAsync(tenantId, DateTime.UtcNow.Date, appCredentials);
                }
            }),
            ("PrivilegedRoles", async () =>
            {
                var privilegedRoles = await _graphService.GetPrivilegedRolesAsync(tenantId);
                if (privilegedRoles.Count > 0)
                {
                    foreach (var r in privilegedRoles) r.TenantName = tenantName;
                    await _dataService.SavePrivilegedRolesAsync(tenantId, DateTime.UtcNow.Date, privilegedRoles);
                }
            }),
            ("DefenderAlerts", async () =>
            {
                var defenderAlerts = await _graphService.GetDefenderAlertsAsync(tenantId);
                if (defenderAlerts.Count > 0)
                {
                    foreach (var a in defenderAlerts) a.TenantName = tenantName;
                    await _dataService.SaveDefenderAlertsAsync(tenantId, DateTime.UtcNow.Date, defenderAlerts);
                }
            }),
            ("DirectoryAudits", async () =>
            {
                // Per-tenant cursor; accumulates history in-DB beyond the short audit-retention window.
                var auditCursor = await _dataService.GetDirectoryAuditCursorAsync(tenantId);
                var (directoryAudits, maxAuditTime) = await _graphService.GetDirectoryAuditsAsync(tenantId, auditCursor);
                if (directoryAudits.Count > 0)
                {
                    foreach (var a in directoryAudits) a.TenantName = tenantName;
                    await _dataService.SaveDirectoryAuditsAsync(directoryAudits);
                }
                if (maxAuditTime.HasValue)
                    await _dataService.UpdateDirectoryAuditCursorAsync(tenantId, maxAuditTime.Value);
            }),
            ("LicenseAssignmentIssues", async () =>
            {
                var licenseIssues = await _graphService.GetLicenseAssignmentIssuesAsync(tenantId, licenses);
                foreach (var l in licenseIssues) l.TenantName = tenantName;
                await _dataService.SaveLicenseAssignmentIssuesAsync(tenantId, DateTime.UtcNow.Date, licenseIssues);
            }),
            ("OAuthGrants", async () =>
            {
                var oauthGrants = await _graphService.GetOAuthGrantsAsync(tenantId);
                foreach (var g in oauthGrants) g.TenantName = tenantName;
                await _dataService.SaveOAuthGrantsAsync(tenantId, DateTime.UtcNow.Date, oauthGrants);
            }),
        };

        // Sign-in summary + legacy-auth/risky sign-in detail, and inactive-account staleness all
        // depend on signInActivity (Entra P1/P2). Queue them only when the tenant is licensed.
        if (entraTier.HasSignInAccess)
        {
            steps.Add(("SignInSummaryAndDetail", async () =>
            {
                var signIns = await _graphService.GetSignInSummaryAsync(tenantId);
                if (signIns.Count > 0)
                    await _dataService.SaveSecuritySummariesAsync(signIns);

                var signInCursor = await _dataService.GetSignInDetailCursorAsync(tenantId);
                var (signInDetail, maxSignInTime) = await _graphService.GetSignInDetailAsync(tenantId, signInCursor);
                if (signInDetail.Count > 0)
                {
                    foreach (var d in signInDetail) d.TenantName = tenantName;
                    await _dataService.SaveSignInDetailAsync(signInDetail);
                }
                if (maxSignInTime.HasValue)
                    await _dataService.UpdateSignInDetailCursorAsync(tenantId, maxSignInTime.Value);
            }));
            steps.Add(("InactiveAccounts", async () =>
            {
                var inactive = await _graphService.GetInactiveAccountsAsync(tenantId);
                if (inactive != null)
                {
                    inactive.TenantName = tenantName;
                    await _dataService.SaveInactiveAccountsAsync(inactive);
                }
            }));
        }
        else
        {
            _logger.LogInformation("Skipping sign-in summary for tenant {TenantName}: requires Microsoft Entra ID P1/P2 (tenant tier: {Tier}).",
                tenantName, entraTier.Tier);
            _logger.LogInformation("Skipping inactive-account analysis for tenant {TenantName}: signInActivity requires Microsoft Entra ID P1/P2 (tenant tier: {Tier}).",
                tenantName, entraTier.Tier);
        }

        // Risky users (Identity Protection — Entra ID P2 only)
        if (entraTier.Tier == "P2")
        {
            steps.Add(("RiskyUsers", async () =>
            {
                var risky = await _graphService.GetRiskyUsersAsync(tenantId);
                if (risky != null)
                {
                    risky.TenantName = tenantName;
                    await _dataService.SaveRiskyUsersAsync(risky);
                }
            }));
        }
        else
        {
            _logger.LogInformation("Skipping risky-user analysis for tenant {TenantName}: Identity Protection requires Microsoft Entra ID P2 (tenant tier: {Tier}).",
                tenantName, entraTier.Tier);
        }

        // Email threat protection (Defender for O365 / EOP — reuses SecurityAlert.Read.All)
        if (defenderTier.HasEmailThreatProtection)
        {
            steps.Add(("EmailThreats", async () =>
            {
                var emailThreats = await _graphService.GetEmailThreatsAsync(tenantId);
                if (emailThreats.Count > 0)
                {
                    foreach (var t in emailThreats) t.TenantName = tenantName;
                    await _dataService.SaveEmailThreatsAsync(tenantId, DateTime.UtcNow.Date, emailThreats);
                }
            }));
        }
        else
        {
            _logger.LogInformation("Skipping email-threat analysis for tenant {TenantName}: requires Microsoft Defender for Office 365 / Exchange Online Protection (Defender tier: {Tier}).",
                tenantName, defenderTier.Tier);
        }

        // Attack Simulation Training (Defender for O365 Plan 2 — requires AttackSimulation.Read.All)
        if (defenderTier.HasAttackSimulation)
        {
            steps.Add(("AttackSimulations", async () =>
            {
                var attackSims = await _graphService.GetAttackSimulationsAsync(tenantId);
                if (attackSims.Count > 0)
                {
                    foreach (var s in attackSims) s.TenantName = tenantName;
                    await _dataService.SaveAttackSimulationsAsync(tenantId, DateTime.UtcNow.Date, attackSims);
                }
            }));
        }
        else
        {
            _logger.LogInformation("Skipping Attack Simulation Training analysis for tenant {TenantName}: requires Microsoft Defender for Office 365 Plan 2 (Defender tier: {Tier}).",
                tenantName, defenderTier.Tier);
        }

        await RunStepsAsync(tenantName, _graphService.GetThrottleSignal(tenantId), steps, ct);

        // --- Phase 2: derived/dependent work. The consumption score aggregates the just-collected
        // storage/activities/segments/licenses (and reads the MAU saved in phase 1), so it runs
        // after the parallel phase completes. ---
        await ComputeConsumptionScoreAsync(tenantId, tenantName, storage, activities, segments, licenses);

        // Probe consent health so the UI can flag tenants that need re-consent
        var missingPermissions = await _graphService.CheckMissingPermissionsAsync(tenantId);
        await _dataService.UpdateTenantPermissionStatusAsync(tenantId, missingPermissions);
        if (missingPermissions.Count > 0)
        {
            _logger.LogWarning("Tenant {TenantName} is missing consent for: {Permissions}",
                tenantName, string.Join(", ", missingPermissions));
        }

        _logger.LogInformation("On-demand data collection completed for tenant {TenantName}", tenantName);
    }

    private async Task ComputeConsumptionScoreAsync(
        string tenantId, string tenantName,
        List<StorageSnapshot> storage,
        List<WorkloadActivitySnapshot> activities,
        List<UserSegmentSnapshot> segments,
        List<LicenseSnapshot> licenses)
    {
        var today = DateTime.UtcNow.Date;
        var totalLicenses = licenses.Sum(l => l.TotalLicenses);
        if (totalLicenses == 0) return;

        var latestMau = await _dataService.GetLatestMauByServiceAsync(new[] { tenantId });
        var m365Active = latestMau.Where(s => s.ServiceName == M365Services.Office365).Sum(s => s.ActiveUserCount);
        var activeUserPct = Math.Min(100.0, (double)m365Active / totalLicenses * 100);

        var totalActions = activities.Sum(a => a.Count);
        var activityIntensity = m365Active > 0 ? Math.Min(100.0, (double)totalActions / m365Active / 10.0) : 0;

        var totalStorageUsed = storage.GroupBy(s => s.ServiceName)
            .Sum(g => g.OrderByDescending(s => s.ReportDate).First().UsedBytes);
        var estimatedAllocated = (long)totalLicenses * 50L * 1024 * 1024 * 1024;
        var storageUtilPct = estimatedAllocated > 0 ? Math.Min(100.0, (double)totalStorageUsed / estimatedAllocated * 100) : 0;

        var latestSegment = segments.OrderByDescending(s => s.ReportDate).FirstOrDefault();
        double avgWorkloads = 0;
        if (latestSegment != null && latestSegment.TotalUsers > 0)
        {
            avgWorkloads = ((double)latestSegment.HeavyUsers * 4 + latestSegment.LightUsers * 1.5) / latestSegment.TotalUsers;
        }
        var breadthPct = Math.Min(100.0, avgWorkloads / 5.0 * 100);

        var score = activeUserPct * 0.30 + activityIntensity * 0.30 + storageUtilPct * 0.20 + breadthPct * 0.20;

        var consumption = new ConsumptionSnapshot
        {
            TenantId = tenantId,
            TenantName = tenantName,
            ReportDate = today,
            StorageUsedBytes = totalStorageUsed,
            StorageAllocatedBytes = estimatedAllocated,
            TotalActivityCount = totalActions,
            ActiveUserCount = m365Active,
            LicensedUserCount = totalLicenses,
            AvgWorkloadsPerUser = avgWorkloads,
            ConsumptionScore = Math.Round(score, 1),
            CollectedAt = DateTime.UtcNow
        };

        await _dataService.SaveConsumptionAsync(consumption);
    }
}
