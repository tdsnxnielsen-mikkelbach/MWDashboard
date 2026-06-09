using MWDashboard.Shared.Services;

namespace MWDashboard.Web.Services;

/// <summary>
/// Delegates data collection to the separate Collector container app via HTTP.
/// Falls back to local collection if the collector is unreachable.
/// </summary>
public class HttpCollectorClient : IDataCollectionService
{
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HttpCollectorClient> _logger;

    public HttpCollectorClient(
        HttpClient httpClient,
        IServiceProvider serviceProvider,
        ILogger<HttpCollectorClient> logger)
    {
        _httpClient = httpClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default)
    {
        try
        {
            var url = $"/collect/{Uri.EscapeDataString(tenantId)}?tenantName={Uri.EscapeDataString(tenantName)}";
            var response = await _httpClient.PostAsync(url, null, ct);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Collection delegated to collector service for tenant {TenantName}", tenantName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Collector service unavailable, falling back to local collection for {TenantName}", tenantName);
            // Fallback: run collection locally
            using var scope = _serviceProvider.CreateScope();
            var localCollector = new MWDashboard.Shared.Services.OnDemandDataCollectionService(
                scope.ServiceProvider.GetRequiredService<IGraphReportService>(),
                scope.ServiceProvider.GetRequiredService<IMauDataService>(),
                scope.ServiceProvider.GetRequiredService<ILogger<MWDashboard.Shared.Services.OnDemandDataCollectionService>>());
            await localCollector.CollectForTenantAsync(tenantId, tenantName, ct);
        }
    }
}
