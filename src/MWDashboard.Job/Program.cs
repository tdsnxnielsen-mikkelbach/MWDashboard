using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MWDashboard.Shared.Data;
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

            var posts = await graphService.GetMessageCenterPostsAsync(tenant.TenantId);
            if (posts.Count > 0)
                await dataService.SaveMessageCenterPostsAsync(posts);

            var signIns = await graphService.GetSignInSummaryAsync(tenant.TenantId);
            if (signIns.Count > 0)
                await dataService.SaveSecuritySummariesAsync(signIns);

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
