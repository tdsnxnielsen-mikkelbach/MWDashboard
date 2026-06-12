using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Services;

namespace MWDashboard.CopilotAudit;

/// <summary>
/// Internal cron-style scheduler that drives Copilot-Chat audit collection on a fixed interval.
/// Running inside the always-on (minReplicas 1) container keeps each tenant's content cursor
/// advancing within the Management Activity API's 7-day retention window, even when no on-demand
/// HTTP request arrives. On-demand collection is still available via the POST /collect endpoint.
/// </summary>
public class CopilotAuditScheduleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<CopilotAuditScheduleService> _logger;

    public CopilotAuditScheduleService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<CopilotAuditScheduleService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = _config.GetValue("CopilotAudit:ScheduleIntervalHours", 24);
        var interval = TimeSpan.FromHours(Math.Max(1, intervalHours));

        // Give the app a moment to finish startup (DB migration etc.) before the first run.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(interval);
        do
        {
            await RunCycleAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MauDbContext>>();
            var collector = scope.ServiceProvider.GetRequiredService<ICopilotAuditCollectionService>();
            var sharingCollector = scope.ServiceProvider.GetRequiredService<IExternalSharingCollectionService>();

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var tenants = await db.Tenants.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct);
            _logger.LogInformation("Scheduled Copilot Chat audit collection starting for {Count} active tenants", tenants.Count);

            foreach (var tenant in tenants)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    await collector.CollectForTenantAsync(tenant.TenantId, tenant.TenantName, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scheduled Copilot Chat audit collection failed for tenant {TenantName} ({TenantId})",
                        tenant.TenantName, tenant.TenantId);
                }

                if (ct.IsCancellationRequested) break;
                try
                {
                    await sharingCollector.CollectForTenantAsync(tenant.TenantId, tenant.TenantName, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (CopilotAuditConfigurationException ex)
                {
                    _logger.LogWarning("External sharing audit not yet available for tenant {TenantName} ({TenantId}): {Message}",
                        tenant.TenantName, tenant.TenantId, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scheduled external sharing audit collection failed for tenant {TenantName} ({TenantId})",
                        tenant.TenantName, tenant.TenantId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — ignore.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled Copilot Chat audit collection cycle failed");
        }
    }
}
