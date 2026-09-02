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

// Application services. The per-tenant collection logic lives in the shared
// OnDemandDataCollectionService (the same code path the on-demand collector uses), so the
// scheduled job stays a thin orchestrator over the active-tenant list.
builder.Services.AddScoped<IGraphReportService, GraphReportService>();
builder.Services.AddScoped<IMauDataService, MauDataService>();
builder.Services.AddScoped<IDataCollectionService, OnDemandDataCollectionService>();

var host = builder.Build();

using var startupScope = host.Services.CreateScope();
var logger = startupScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
var dbFactory = startupScope.ServiceProvider.GetRequiredService<IDbContextFactory<MauDbContext>>();
var config = startupScope.ServiceProvider.GetRequiredService<IConfiguration>();

logger.LogInformation("MAU Snapshot Collection Job started");

try
{
    await using var db = await dbFactory.CreateDbContextAsync();

    // Ensure database is migrated
    await db.Database.MigrateAsync();

    var tenants = await db.Tenants.Where(t => t.IsActive).ToListAsync();
    logger.LogInformation("Found {Count} active tenants", tenants.Count);

    // Collect tenants in parallel with a bounded degree of concurrency. Microsoft Graph
    // throttles per-app and per-tenant, so we cap concurrency (default 4) to shrink the overall
    // collection window without provoking 429s. Each tenant runs in its own DI scope so its
    // scoped GraphReportService (per-tenant client/token cache) and MauDataService are isolated.
    var maxParallel = Math.Max(1, config.GetValue("Collection:MaxParallelTenants", 4));
    using var throttle = new SemaphoreSlim(maxParallel);

    var tasks = tenants.Select(async tenant =>
    {
        await throttle.WaitAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            var collector = scope.ServiceProvider.GetRequiredService<IDataCollectionService>();
            logger.LogInformation("Collecting data for tenant {TenantName} ({TenantId})", tenant.TenantName, tenant.TenantId);
            await collector.CollectForTenantAsync(tenant.TenantId, tenant.TenantName);
            logger.LogInformation("Data collection completed for tenant {TenantName}", tenant.TenantName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error collecting data for tenant {TenantName}", tenant.TenantName);
        }
        finally
        {
            throttle.Release();
        }
    });

    await Task.WhenAll(tasks);

    logger.LogInformation("Snapshot collection completed for {Count} tenants", tenants.Count);
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Fatal error during data collection job");
    Environment.ExitCode = 1;
}
