using Microsoft.EntityFrameworkCore;
using MWDashboard.Data;
using MWDashboard.Models;

namespace MWDashboard.Services;

public interface IDataCollectionService
{
    Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default);
}

public class MauSnapshotBackgroundService : BackgroundService, IDataCollectionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MauSnapshotBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(24);

    public MauSnapshotBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<MauSnapshotBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MAU Snapshot Background Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectAllAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during MAU snapshot collection");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CollectAllAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MauDbContext>>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenants = await db.Tenants.Where(t => t.IsActive).ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            await CollectForTenantAsync(tenant.TenantId, tenant.TenantName, ct);
        }

        _logger.LogInformation("Snapshot collection completed for {Count} tenants", tenants.Count);
    }

    public async Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default)
    {
        _logger.LogInformation("Collecting data for tenant {TenantName} ({TenantId})", tenantName, tenantId);

        using var scope = _scopeFactory.CreateScope();
        var graphService = scope.ServiceProvider.GetRequiredService<IGraphReportService>();
        var dataService = scope.ServiceProvider.GetRequiredService<IMauDataService>();

        var snapshots = await graphService.GetActiveUserCountsAsync(tenantId);
        if (snapshots.Count > 0)
        {
            foreach (var s in snapshots)
                s.TenantName = tenantName;

            await dataService.SaveSnapshotsAsync(snapshots);
        }

        var licenses = await graphService.GetSubscribedSkusAsync(tenantId);
        if (licenses.Count > 0)
            await dataService.SaveLicensesAsync(licenses);

        // Collect Message Center posts
        var posts = await graphService.GetMessageCenterPostsAsync(tenantId);
        if (posts.Count > 0)
            await dataService.SaveMessageCenterPostsAsync(posts);

        // Collect security sign-in summaries
        var signIns = await graphService.GetSignInSummaryAsync(tenantId);
        if (signIns.Count > 0)
            await dataService.SaveSecuritySummariesAsync(signIns);

        _logger.LogInformation("Data collection completed for tenant {TenantName}", tenantName);
    }
}
