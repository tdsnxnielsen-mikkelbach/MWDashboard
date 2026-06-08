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

        // Compute and save consumption score
        await ComputeConsumptionScoreAsync(tenantId, tenantName, storage, activities, segments, licenses);

        _logger.LogInformation("On-demand data collection completed for tenant {TenantName}", tenantName);
    }

    private async Task ComputeConsumptionScoreAsync(
        string tenantId, string tenantName,
        List<MWDashboard.Shared.Models.StorageSnapshot> storage,
        List<MWDashboard.Shared.Models.WorkloadActivitySnapshot> activities,
        List<MWDashboard.Shared.Models.UserSegmentSnapshot> segments,
        List<MWDashboard.Shared.Models.LicenseSnapshot> licenses)
    {
        var today = DateTime.UtcNow.Date;
        var totalLicenses = licenses.Sum(l => l.TotalLicenses);
        if (totalLicenses == 0) return;

        // Active user % (from latest MAU - use licenses consumed as proxy for active across services)
        var latestMau = await _dataService.GetLatestMauByServiceAsync(new[] { tenantId });
        var m365Active = latestMau.Where(s => s.ServiceName == "Office 365").Sum(s => s.ActiveUserCount);
        var activeUserPct = Math.Min(100.0, (double)m365Active / totalLicenses * 100);

        // Activity intensity (total actions / active users)
        var totalActions = activities.Sum(a => a.Count);
        var activityIntensity = m365Active > 0 ? Math.Min(100.0, (double)totalActions / m365Active / 10.0) : 0;

        // Storage utilization
        var totalStorageUsed = storage.GroupBy(s => s.ServiceName)
            .Sum(g => g.OrderByDescending(s => s.ReportDate).First().UsedBytes);
        // Use a reasonable baseline: 50GB per user allocated
        var estimatedAllocated = (long)totalLicenses * 50L * 1024 * 1024 * 1024;
        var storageUtilPct = estimatedAllocated > 0 ? Math.Min(100.0, (double)totalStorageUsed / estimatedAllocated * 100) : 0;

        // Workload breadth (from segmentation)
        var latestSegment = segments.OrderByDescending(s => s.ReportDate).FirstOrDefault();
        double avgWorkloads = 0;
        if (latestSegment != null && latestSegment.TotalUsers > 0)
        {
            // Heavy = 3+ workloads (avg 4), Light = 1-2 (avg 1.5), Inactive = 0
            avgWorkloads = ((double)latestSegment.HeavyUsers * 4 + latestSegment.LightUsers * 1.5) / latestSegment.TotalUsers;
            // Normalize to 0-100 (max 5 workloads)
        }
        var breadthPct = Math.Min(100.0, avgWorkloads / 5.0 * 100);

        // Composite score: weighted average
        var score = activeUserPct * 0.30 + activityIntensity * 0.30 + storageUtilPct * 0.20 + breadthPct * 0.20;

        var consumption = new MWDashboard.Shared.Models.ConsumptionSnapshot
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
