using MWDashboard.Shared.Services;

namespace MWDashboard.Web.Services;

/// <summary>
/// Triggers an on-demand Copilot Chat (unlicensed) audit collection for a single tenant.
/// </summary>
public interface ICopilotAuditClient
{
    Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default);
}

/// <summary>
/// Delegates Copilot Chat audit collection to the separate <c>MWDashboard.CopilotAudit</c>
/// container app via HTTP. Falls back to running the collection locally if that service is
/// unreachable (mirrors <see cref="HttpCollectorClient"/>).
/// </summary>
public class HttpCopilotAuditClient : ICopilotAuditClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HttpCopilotAuditClient> _logger;

    public HttpCopilotAuditClient(
        HttpClient httpClient,
        IServiceProvider serviceProvider,
        ILogger<HttpCopilotAuditClient> logger)
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
            _logger.LogInformation("Copilot Chat audit collection delegated to copilotaudit service for tenant {TenantName}", tenantName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot audit service unavailable, falling back to local collection for {TenantName}", tenantName);
            using var scope = _serviceProvider.CreateScope();
            var localCollector = scope.ServiceProvider.GetRequiredService<ICopilotAuditCollectionService>();
            await localCollector.CollectForTenantAsync(tenantId, tenantName, ct);
        }
    }
}

/// <summary>
/// Runs Copilot Chat audit collection in-process (used when no copilotaudit container URL is
/// configured, e.g. local development).
/// </summary>
public class LocalCopilotAuditClient : ICopilotAuditClient
{
    private readonly ICopilotAuditCollectionService _service;

    public LocalCopilotAuditClient(ICopilotAuditCollectionService service)
    {
        _service = service;
    }

    public Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default)
        => _service.CollectForTenantAsync(tenantId, tenantName, ct);
}
