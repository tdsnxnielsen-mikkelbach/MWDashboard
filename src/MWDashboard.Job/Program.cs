using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Models;
using MWDashboard.Shared.Services;
using Azure.Monitor.OpenTelemetry.AspNetCore;

var builder = Host.CreateApplicationBuilder(args);

// OpenTelemetry with Azure Monitor (distributed tracing for the job)
var aiConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrEmpty(aiConnectionString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
    {
        options.ConnectionString = aiConnectionString;
    });
}

// EF Core with SQL Server
builder.Services.AddDbContextFactory<MauDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), sqlOptions =>
    {
        sqlOptions.CommandTimeout(120);
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: [-2, 4060]);
    }));

// Application services
builder.Services.AddScoped<IGraphReportService, GraphReportService>();
builder.Services.AddScoped<IMauDataService, MauDataService>();

var host = builder.Build();

// Run data collection as a one-shot job
using var scope = host.Services.CreateScope();
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MauDbContext>>();
var graphService = scope.ServiceProvider.GetRequiredService<IGraphReportService>();
var dataService = scope.ServiceProvider.GetRequiredService<IMauDataService>();

logger.LogInformation("MAU Snapshot Collection Job started");

try
{
    await using var db = await dbFactory.CreateDbContextAsync();

    // Ensure database is migrated
    await db.Database.MigrateAsync();

    var tenants = await db.Tenants.Where(t => t.IsActive).ToListAsync();
    logger.LogInformation("Found {Count} active tenants", tenants.Count);

    foreach (var tenant in tenants)
    {
        logger.LogInformation("Collecting data for tenant {TenantName} ({TenantId})", tenant.TenantName, tenant.TenantId);

        try
        {
            var snapshots = await graphService.GetActiveUserCountsAsync(tenant.TenantId);
            if (snapshots.Count > 0)
            {
                foreach (var s in snapshots)
                    s.TenantName = tenant.TenantName;
                await dataService.SaveSnapshotsAsync(snapshots);
            }

            var licenses = await graphService.GetSubscribedSkusAsync(tenant.TenantId);
            if (licenses.Count > 0)
                await dataService.SaveLicensesAsync(licenses);

            // Determine the tenant's Entra ID tier from its SKUs. Sign-in-based features
            // (signInActivity, sign-in logs) require P1/P2, so skip them on the free tier
            // instead of issuing a call that always returns 403.
            var entraTier = TenantEntraTier.FromLicenses(tenant.TenantId, tenant.TenantName, licenses.Select(l => l.SkuPartNumber));

            var posts = await graphService.GetMessageCenterPostsAsync(tenant.TenantId);
            if (posts.Count > 0)
                await dataService.SaveMessageCenterPostsAsync(posts);

            if (entraTier.HasSignInAccess)
            {
                var signIns = await graphService.GetSignInSummaryAsync(tenant.TenantId);
                if (signIns.Count > 0)
                    await dataService.SaveSecuritySummariesAsync(signIns);
            }
            else
            {
                logger.LogInformation("Skipping sign-in summary for tenant {TenantName}: requires Microsoft Entra ID P1/P2 (tenant tier: {Tier}).",
                    tenant.TenantName, entraTier.Tier);
            }

            var activities = await graphService.GetWorkloadActivityAsync(tenant.TenantId);
            if (activities.Count > 0)
            {
                foreach (var a in activities)
                    a.TenantName = tenant.TenantName;
                await dataService.SaveWorkloadActivityAsync(activities);
            }

            var copilot = await graphService.GetCopilotUsageAsync(tenant.TenantId);
            if (copilot.Count > 0)
            {
                foreach (var c in copilot)
                    c.TenantName = tenant.TenantName;
                await dataService.SaveCopilotUsageAsync(copilot);
            }

            var segments = await graphService.GetUserSegmentationAsync(tenant.TenantId);
            if (segments.Count > 0)
            {
                foreach (var s in segments)
                    s.TenantName = tenant.TenantName;
                await dataService.SaveUserSegmentsAsync(segments);
            }

            var depts = await graphService.GetDepartmentUsageAsync(tenant.TenantId);
            if (depts.Count > 0)
            {
                foreach (var d in depts)
                    d.TenantName = tenant.TenantName;
                await dataService.SaveDepartmentUsageAsync(depts);
            }

            // Storage usage
            var storage = await graphService.GetStorageUsageAsync(tenant.TenantId);
            if (storage.Count > 0)
            {
                foreach (var s in storage)
                    s.TenantName = tenant.TenantName;
                await dataService.SaveStorageAsync(storage);
            }

            // M365 App Platform usage
            var appUsage = await graphService.GetM365AppUsageAsync(tenant.TenantId);
            if (appUsage.Count > 0)
            {
                foreach (var a in appUsage)
                    a.TenantName = tenant.TenantName;
                await dataService.SaveM365AppUsageAsync(appUsage);
            }

            // Microsoft Secure Score (daily score trend + per-control remediation actions)
            var (secureScores, secureControls) = await graphService.GetSecureScoreAsync(tenant.TenantId);
            if (secureScores.Count > 0)
            {
                foreach (var s in secureScores)
                    s.TenantName = tenant.TenantName;
                await dataService.SaveSecureScoresAsync(secureScores);
            }
            if (secureControls.Count > 0)
            {
                foreach (var c in secureControls)
                    c.TenantName = tenant.TenantName;
                await dataService.SaveSecureScoreControlsAsync(secureControls);
            }

            // MFA / authentication method registration (tenant-level adoption counts)
            var mfa = await graphService.GetMfaRegistrationAsync(tenant.TenantId);
            if (mfa != null)
            {
                mfa.TenantName = tenant.TenantName;
                await dataService.SaveMfaRegistrationAsync(mfa);
            }

            // Inactive / stale licensed accounts (tenant-level staleness counts)
            if (entraTier.HasSignInAccess)
            {
                var inactive = await graphService.GetInactiveAccountsAsync(tenant.TenantId);
                if (inactive != null)
                {
                    inactive.TenantName = tenant.TenantName;
                    await dataService.SaveInactiveAccountsAsync(inactive);
                }
            }
            else
            {
                logger.LogInformation("Skipping inactive-account analysis for tenant {TenantName}: signInActivity requires Microsoft Entra ID P1/P2 (tenant tier: {Tier}).",
                    tenant.TenantName, entraTier.Tier);
            }

            // Service health overview + active issues
            var (healthServices, healthIssues) = await graphService.GetServiceHealthAsync(tenant.TenantId);
            if (healthServices.Count > 0)
            {
                foreach (var s in healthServices)
                    s.TenantName = tenant.TenantName;
                await dataService.SaveServiceHealthAsync(healthServices);
            }
            foreach (var i in healthIssues)
                i.TenantName = tenant.TenantName;
            await dataService.SaveServiceHealthIssuesAsync(healthIssues);

            // Intune device compliance (all tiers)
            var deviceCompliance = await graphService.GetDeviceComplianceAsync(tenant.TenantId);
            if (deviceCompliance != null)
            {
                deviceCompliance.TenantName = tenant.TenantName;
                await dataService.SaveDeviceComplianceAsync(deviceCompliance);
            }

            // Conditional Access coverage (all tiers)
            var conditionalAccess = await graphService.GetConditionalAccessAsync(tenant.TenantId);
            if (conditionalAccess != null)
            {
                conditionalAccess.TenantName = tenant.TenantName;
                await dataService.SaveConditionalAccessAsync(conditionalAccess);
            }

            // Guest / external users (all tiers — uses User.Read.All)
            var guests = await graphService.GetGuestUsersAsync(tenant.TenantId);
            if (guests != null)
            {
                guests.TenantName = tenant.TenantName;
                await dataService.SaveGuestUsersAsync(guests);
            }

            // Risky users (Identity Protection — Entra ID P2 only)
            if (entraTier.Tier == "P2")
            {
                var risky = await graphService.GetRiskyUsersAsync(tenant.TenantId);
                if (risky != null)
                {
                    risky.TenantName = tenant.TenantName;
                    await dataService.SaveRiskyUsersAsync(risky);
                }
            }
            else
            {
                logger.LogInformation("Skipping risky-user analysis for tenant {TenantName}: Identity Protection requires Microsoft Entra ID P2 (tenant tier: {Tier}).",
                    tenant.TenantName, entraTier.Tier);
            }

            // --- Tier 3: Usage & Governance (all tiers) ---

            // Mailbox usage (aggregate + top-N largest mailboxes)
            var (mailboxAgg, topMailboxes) = await graphService.GetMailboxUsageAsync(tenant.TenantId);
            if (mailboxAgg != null)
            {
                mailboxAgg.TenantName = tenant.TenantName;
                await dataService.SaveMailboxUsageAsync(mailboxAgg);
            }
            if (topMailboxes.Count > 0)
            {
                foreach (var m in topMailboxes) m.TenantName = tenant.TenantName;
                await dataService.SaveTopMailboxesAsync(topMailboxes);
            }

            // Teams device usage
            var teamsDevices = await graphService.GetTeamsDeviceUsageAsync(tenant.TenantId);
            if (teamsDevices != null)
            {
                teamsDevices.TenantName = tenant.TenantName;
                await dataService.SaveTeamsDeviceUsageAsync(teamsDevices);
            }

            // SharePoint / OneDrive site usage (aggregate + top-N detail)
            var (siteAggs, siteDetails) = await graphService.GetSiteUsageAsync(tenant.TenantId);
            if (siteAggs.Count > 0)
            {
                foreach (var s in siteAggs) s.TenantName = tenant.TenantName;
                await dataService.SaveSiteUsageAsync(siteAggs);
            }
            if (siteDetails.Count > 0)
            {
                foreach (var s in siteDetails) s.TenantName = tenant.TenantName;
                await dataService.SaveSiteUsageDetailAsync(siteDetails);
            }

            // Viva Engage (Yammer) activity
            var yammer = await graphService.GetYammerActivityAsync(tenant.TenantId);
            if (yammer != null)
            {
                yammer.TenantName = tenant.TenantName;
                await dataService.SaveYammerActivityAsync(yammer);
            }

            // Groups & Teams sprawl (requires Group.Read.All)
            var groups = await graphService.GetGroupSprawlAsync(tenant.TenantId);
            if (groups != null)
            {
                groups.TenantName = tenant.TenantName;
                await dataService.SaveGroupSprawlAsync(groups);
            }

            // App registration / service-principal credential expiry (requires Application.Read.All)
            var appCredentials = await graphService.GetAppCredentialsAsync(tenant.TenantId);
            if (appCredentials.Count > 0)
            {
                foreach (var c in appCredentials) c.TenantName = tenant.TenantName;
                await dataService.SaveAppCredentialsAsync(tenant.TenantId, DateTime.UtcNow.Date, appCredentials);
            }

            // Privileged role inventory (requires RoleManagement.Read.Directory)
            var privilegedRoles = await graphService.GetPrivilegedRolesAsync(tenant.TenantId);
            if (privilegedRoles.Count > 0)
            {
                foreach (var r in privilegedRoles) r.TenantName = tenant.TenantName;
                await dataService.SavePrivilegedRolesAsync(tenant.TenantId, DateTime.UtcNow.Date, privilegedRoles);
            }

            // Defender / M365 security alerts (requires SecurityAlert.Read.All)
            var defenderAlerts = await graphService.GetDefenderAlertsAsync(tenant.TenantId);
            if (defenderAlerts.Count > 0)
            {
                foreach (var a in defenderAlerts) a.TenantName = tenant.TenantName;
                await dataService.SaveDefenderAlertsAsync(tenant.TenantId, DateTime.UtcNow.Date, defenderAlerts);
            }

            // Compute consumption score
            {
                var totalLicenses = licenses.Sum(l => l.TotalLicenses);
                if (totalLicenses > 0)
                {
                    var latestMau = await dataService.GetLatestMauByServiceAsync(new[] { tenant.TenantId });
                    var m365Active = latestMau.Where(s => s.ServiceName == "Office 365").Sum(s => s.ActiveUserCount);
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

                    await dataService.SaveConsumptionAsync(new MWDashboard.Shared.Models.ConsumptionSnapshot
                    {
                        TenantId = tenant.TenantId,
                        TenantName = tenant.TenantName,
                        ReportDate = DateTime.UtcNow.Date,
                        StorageUsedBytes = totalStorageUsed,
                        StorageAllocatedBytes = estimatedAllocated,
                        TotalActivityCount = totalActions,
                        ActiveUserCount = m365Active,
                        LicensedUserCount = totalLicenses,
                        AvgWorkloadsPerUser = avgWorkloads,
                        ConsumptionScore = Math.Round(score, 1),
                        CollectedAt = DateTime.UtcNow
                    });
                }
            }

            // Probe consent health so the UI can flag tenants that need re-consent
            var missingPermissions = await graphService.CheckMissingPermissionsAsync(tenant.TenantId);
            await dataService.UpdateTenantPermissionStatusAsync(tenant.TenantId, missingPermissions);
            if (missingPermissions.Count > 0)
            {
                logger.LogWarning("Tenant {TenantName} is missing consent for: {Permissions}",
                    tenant.TenantName, string.Join(", ", missingPermissions));
            }

            logger.LogInformation("Data collection completed for tenant {TenantName}", tenant.TenantName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error collecting data for tenant {TenantName}", tenant.TenantName);
        }
    }

    logger.LogInformation("Snapshot collection completed for {Count} tenants", tenants.Count);
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Fatal error during data collection job");
    Environment.ExitCode = 1;
}
