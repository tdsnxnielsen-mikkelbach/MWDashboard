using MWDashboard.Shared.Services;

namespace MWDashboard.Web.Services;

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

        var posts = await _graphService.GetMessageCenterPostsAsync(tenantId);
        if (posts.Count > 0)
            await _dataService.SaveMessageCenterPostsAsync(posts);

        var signIns = await _graphService.GetSignInSummaryAsync(tenantId);
        if (signIns.Count > 0)
            await _dataService.SaveSecuritySummariesAsync(signIns);

        _logger.LogInformation("On-demand data collection completed for tenant {TenantName}", tenantName);
    }
}
