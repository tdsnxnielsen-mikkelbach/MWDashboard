using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Services;

var builder = Host.CreateApplicationBuilder(args);

// EF Core with SQL Server
builder.Services.AddDbContextFactory<MauDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), sqlOptions =>
    {
        sqlOptions.CommandTimeout(60);
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
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
