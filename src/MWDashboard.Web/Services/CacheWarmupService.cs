using MWDashboard.Shared.Services;

namespace MWDashboard.Web.Services;

/// <summary>
/// Pre-populates the cache with common queries on application startup to avoid
/// thundering herd on SQL when replicas cold-start simultaneously.
/// </summary>
public class CacheWarmupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CacheWarmupService> _logger;

    public CacheWarmupService(IServiceProvider serviceProvider, ILogger<CacheWarmupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to let the app fully start before warming cache
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        _logger.LogInformation("Cache warm-up starting");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dataService = scope.ServiceProvider.GetRequiredService<IMauDataService>();

            // Warm the most common dashboard queries (all-tenant views)
            await dataService.GetLatestMauByServiceAsync((IEnumerable<string>?)null);
            await dataService.GetConsumptionAsync(null, months: 6);
            await dataService.GetLatestLicensesAsync((IEnumerable<string>?)null);
            await dataService.GetWorkloadActivityAsync(null, days: 30);
            await dataService.GetSecuritySummaryAsync((IEnumerable<string>?)null, days: 30);
            await dataService.GetStorageAsync(null, days: 30);
            await dataService.GetM365AppUsageAsync(null);
            await dataService.GetCopilotUsageAsync(null);
            await dataService.GetUserSegmentsAsync(null, months: 6);
            await dataService.GetDepartmentUsageAsync(null);
            await dataService.GetSecureScoresAsync(null, days: 90);
            await dataService.GetSecureScoreControlsAsync(null);
            await dataService.GetMfaRegistrationAsync(null);
            await dataService.GetInactiveAccountsAsync(null);
            await dataService.GetServiceHealthAsync(null);
            await dataService.GetServiceHealthIssuesAsync(null);
            await dataService.GetDeviceComplianceAsync(null);
            await dataService.GetConditionalAccessAsync(null);
            await dataService.GetGuestUsersAsync(null);
            await dataService.GetRiskyUsersAsync(null);
            await dataService.GetMailboxUsageAsync(null);
            await dataService.GetTopMailboxesAsync(null);
            await dataService.GetTeamsDeviceUsageAsync(null);
            await dataService.GetSiteUsageAsync(null);
            await dataService.GetSiteUsageDetailAsync(null);
            await dataService.GetYammerActivityAsync(null);
            await dataService.GetGroupSprawlAsync(null);

            _logger.LogInformation("Cache warm-up completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache warm-up failed (non-critical, queries will cache on first access)");
        }
    }
}
