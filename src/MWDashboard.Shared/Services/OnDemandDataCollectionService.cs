using Microsoft.Extensions.Logging;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public class OnDemandDataCollectionService : IDataCollectionService
{
    private readonly IGraphReportService _graphService;
    private readonly IMauDataService _dataService;
    private readonly ILogger<OnDemandDataCollectionService> _logger;

    public OnDemandDataCollectionService(
        IGraphReportService graphService,
        IMauDataService dataService,
        ILogger<OnDemandDataCollectionService> logger)
    {
        _graphService = graphService;
        _dataService = dataService;
        _logger = logger;
    }

    public async Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default)
    {
        _logger.LogInformation("On-demand data collection for tenant {TenantName} ({TenantId})", tenantName, tenantId);

        var snapshots = await _graphService.GetActiveUserCountsAsync(tenantId);
        if (snapshots.Count > 0)
        {
            foreach (var s in snapshots)
                s.TenantName = tenantName;
            await _dataService.SaveSnapshotsAsync(snapshots);
        }

        var licenses = await _graphService.GetSubscribedSkusAsync(tenantId);
        if (licenses.Count > 0)
            await _dataService.SaveLicensesAsync(licenses);

        // Determine the tenant's Entra ID tier from its SKUs. Sign-in-based features
        // (signInActivity, sign-in logs) require P1/P2, so skip them on the free tier
        // instead of issuing a call that always returns 403.
        var entraTier = TenantEntraTier.FromLicenses(tenantId, tenantName, licenses.Select(l => l.SkuPartNumber));

        var posts = await _graphService.GetMessageCenterPostsAsync(tenantId);
        if (posts.Count > 0)
            await _dataService.SaveMessageCenterPostsAsync(posts);

        if (entraTier.HasSignInAccess)
        {
            var signIns = await _graphService.GetSignInSummaryAsync(tenantId);
            if (signIns.Count > 0)
                await _dataService.SaveSecuritySummariesAsync(signIns);
        }
        else
        {
            _logger.LogInformation("Skipping sign-in summary for tenant {TenantName}: requires Microsoft Entra ID P1/P2 (tenant tier: {Tier}).",
                tenantName, entraTier.Tier);
        }

        var activities = await _graphService.GetWorkloadActivityAsync(tenantId);
        if (activities.Count > 0)
        {
            foreach (var a in activities)
                a.TenantName = tenantName;
            await _dataService.SaveWorkloadActivityAsync(activities);
        }

        var copilot = await _graphService.GetCopilotUsageAsync(tenantId);
        if (copilot.Count > 0)
        {
            foreach (var c in copilot)
                c.TenantName = tenantName;
            await _dataService.SaveCopilotUsageAsync(copilot);
        }

        var segments = await _graphService.GetUserSegmentationAsync(tenantId);
        if (segments.Count > 0)
        {
            foreach (var s in segments)
                s.TenantName = tenantName;
            await _dataService.SaveUserSegmentsAsync(segments);
        }

        var depts = await _graphService.GetDepartmentUsageAsync(tenantId);
        if (depts.Count > 0)
        {
            foreach (var d in depts)
                d.TenantName = tenantName;
            await _dataService.SaveDepartmentUsageAsync(depts);
        }

        // Storage usage
        var storage = await _graphService.GetStorageUsageAsync(tenantId);
        if (storage.Count > 0)
        {
            foreach (var s in storage)
                s.TenantName = tenantName;
            await _dataService.SaveStorageAsync(storage);
        }

        // M365 App Platform usage
        var appUsage = await _graphService.GetM365AppUsageAsync(tenantId);
        if (appUsage.Count > 0)
        {
            foreach (var a in appUsage)
                a.TenantName = tenantName;
            await _dataService.SaveM365AppUsageAsync(appUsage);
        }

        // Microsoft Secure Score (daily score trend + per-control remediation actions)
        var (secureScores, secureControls) = await _graphService.GetSecureScoreAsync(tenantId);
        if (secureScores.Count > 0)
        {
            foreach (var s in secureScores)
                s.TenantName = tenantName;
            await _dataService.SaveSecureScoresAsync(secureScores);
        }
        if (secureControls.Count > 0)
        {
            foreach (var c in secureControls)
                c.TenantName = tenantName;
            await _dataService.SaveSecureScoreControlsAsync(secureControls);
        }

        // MFA / authentication method registration (tenant-level adoption counts)
        var mfa = await _graphService.GetMfaRegistrationAsync(tenantId);
        if (mfa != null)
        {
            mfa.TenantName = tenantName;
            await _dataService.SaveMfaRegistrationAsync(mfa);
        }

        // Inactive / stale licensed accounts (tenant-level staleness counts)
        if (entraTier.HasSignInAccess)
        {
            var inactive = await _graphService.GetInactiveAccountsAsync(tenantId);
            if (inactive != null)
            {
                inactive.TenantName = tenantName;
                await _dataService.SaveInactiveAccountsAsync(inactive);
            }
        }
        else
        {
            _logger.LogInformation("Skipping inactive-account analysis for tenant {TenantName}: signInActivity requires Microsoft Entra ID P1/P2 (tenant tier: {Tier}).",
                tenantName, entraTier.Tier);
        }

        // Service health overview + active issues
        var (healthServices, healthIssues) = await _graphService.GetServiceHealthAsync(tenantId);
        if (healthServices.Count > 0)
        {
            foreach (var s in healthServices)
                s.TenantName = tenantName;
            await _dataService.SaveServiceHealthAsync(healthServices);
        }
        foreach (var i in healthIssues)
            i.TenantName = tenantName;
        await _dataService.SaveServiceHealthIssuesAsync(healthIssues);

        // Intune device compliance (all tiers)
        var deviceCompliance = await _graphService.GetDeviceComplianceAsync(tenantId);
        if (deviceCompliance != null)
        {
            deviceCompliance.TenantName = tenantName;
            await _dataService.SaveDeviceComplianceAsync(deviceCompliance);
        }

        // Conditional Access coverage (all tiers)
        var conditionalAccess = await _graphService.GetConditionalAccessAsync(tenantId);
        if (conditionalAccess != null)
        {
            conditionalAccess.TenantName = tenantName;
            await _dataService.SaveConditionalAccessAsync(conditionalAccess);
        }

        // Guest / external users (all tiers — uses User.Read.All)
        var guests = await _graphService.GetGuestUsersAsync(tenantId);
        if (guests != null)
        {
            guests.TenantName = tenantName;
            await _dataService.SaveGuestUsersAsync(guests);
        }

        // Risky users (Identity Protection — Entra ID P2 only)
        if (entraTier.Tier == "P2")
        {
            var risky = await _graphService.GetRiskyUsersAsync(tenantId);
            if (risky != null)
            {
                risky.TenantName = tenantName;
                await _dataService.SaveRiskyUsersAsync(risky);
            }
        }
        else
        {
            _logger.LogInformation("Skipping risky-user analysis for tenant {TenantName}: Identity Protection requires Microsoft Entra ID P2 (tenant tier: {Tier}).",
                tenantName, entraTier.Tier);
        }

        // --- Tier 3: Usage & Governance (all tiers) ---

        // Mailbox usage (aggregate + top-N largest mailboxes)
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

        // Teams device usage
        var teamsDevices = await _graphService.GetTeamsDeviceUsageAsync(tenantId);
        if (teamsDevices != null)
        {
            teamsDevices.TenantName = tenantName;
            await _dataService.SaveTeamsDeviceUsageAsync(teamsDevices);
        }

        // SharePoint / OneDrive site usage (aggregate + top-N detail)
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

        // Viva Engage (Yammer) activity
        var yammer = await _graphService.GetYammerActivityAsync(tenantId);
        if (yammer != null)
        {
            yammer.TenantName = tenantName;
            await _dataService.SaveYammerActivityAsync(yammer);
        }

        // Groups & Teams sprawl (requires Group.Read.All)
        var groups = await _graphService.GetGroupSprawlAsync(tenantId);
        if (groups != null)
        {
            groups.TenantName = tenantName;
            await _dataService.SaveGroupSprawlAsync(groups);
        }

        // Compute and save consumption score
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
