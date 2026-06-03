using Microsoft.EntityFrameworkCore;
using MWDashboard.Data;

namespace MWDashboard.Services;

public class MauSnapshotBackgroundService : BackgroundService
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
                await CollectSnapshotsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during MAU snapshot collection");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CollectSnapshotsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var graphService = scope.ServiceProvider.GetRequiredService<IGraphReportService>();
        var dataService = scope.ServiceProvider.GetRequiredService<IMauDataService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MauDbContext>>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenants = await db.Tenants.Where(t => t.IsActive).ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            _logger.LogInformation("Collecting MAU data for tenant {TenantName}", tenant.TenantName);

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
        }

        _logger.LogInformation("Snapshot collection completed for {Count} tenants", tenants.Count);
    }
}
