using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MWDashboard.Shared.Services;

/// <summary>
/// Typed client for the TD SYNNEX <b>Ion Gateway</b> — the enrichment hub that fuses StreamOne (Ion)
/// cost/margin and Partner Center (PCT) list price. MWDashboard uses it to decorate the Microsoft
/// license data it already holds (keyed by Entra tenant GUID) with pricing it does not, and to learn
/// about ION/PCT customers it has not onboarded.
///
/// The gateway is authenticated with a per-caller <c>X-Api-Key</c> (format <c>{keyId}.{secret}</c>),
/// stored in Key Vault / user-secrets — never in source. All endpoints live under <c>/api/v1</c>.
/// </summary>
public interface IIonGatewayClient
{
    /// <summary>True when a base URL and API key are configured; false disables all ION features.</summary>
    bool Enabled { get; }

    /// <summary>The indirect-reseller directory (master data): account id, name, contact, delegated-admin flag.</summary>
    Task<IReadOnlyList<IonResellerDto>> GetResellersAsync(string? search = null, CancellationToken ct = default);

    /// <summary>
    /// Every addressable customer across Ion + PCT (auto-paged). Each row carries the Entra tenant GUID,
    /// domain, upstream <c>source</c> (ion|pct), owning Ion account id and reseller name.
    /// </summary>
    Task<IReadOnlyList<IonDirectoryItemDto>> GetCustomerDirectoryAsync(string? source = null, CancellationToken ct = default);

    /// <summary>
    /// Bulk fused pricing (PCT list price + Ion cost/margin) for the given customer keys (tenant GUID or
    /// domain). Automatically chunked into requests of ≤500 keys. Keys that don't resolve are dropped.
    /// </summary>
    Task<IReadOnlyList<IonFusedPricingDto>> GetFusedPricingAsync(IEnumerable<string> keys, CancellationToken ct = default);

    /// <summary>A reseller's crawled orders with line-level realised price/cost/margin, keyed by Ion account id.</summary>
    Task<IReadOnlyList<IonResellerOrdersDto>> GetResellerOrdersAsync(long accountId, CancellationToken ct = default);
}

public sealed class IonGatewayClient : IIonGatewayClient
{
    private const int MaxBatchKeys = 500;

    private readonly HttpClient _http;
    private readonly ILogger<IonGatewayClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public IonGatewayClient(HttpClient http, IConfiguration config, ILogger<IonGatewayClient> logger)
    {
        _http = http;
        _logger = logger;
        var baseUrl = config["IonGateway:BaseUrl"];
        var apiKey = config["IonGateway:ApiKey"];
        Enabled = !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(apiKey);
        if (Enabled)
        {
            _http.BaseAddress = new Uri(baseUrl!.TrimEnd('/') + "/");
            if (!_http.DefaultRequestHeaders.Contains("X-Api-Key"))
                _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            _http.Timeout = TimeSpan.FromMinutes(2);
        }
    }

    public bool Enabled { get; }

    public async Task<IReadOnlyList<IonResellerDto>> GetResellersAsync(string? search = null, CancellationToken ct = default)
    {
        if (!Enabled) return [];
        var url = "api/v1/resellers" + (string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}");
        var result = await _http.GetFromJsonAsync<List<IonResellerDto>>(url, JsonOptions, ct);
        return result ?? [];
    }

    public async Task<IReadOnlyList<IonDirectoryItemDto>> GetCustomerDirectoryAsync(string? source = null, CancellationToken ct = default)
    {
        if (!Enabled) return [];
        var items = new List<IonDirectoryItemDto>();
        string? pageToken = null;
        var safety = 0;
        do
        {
            var url = "api/v1/customers/directory";
            var q = new List<string>();
            if (!string.IsNullOrWhiteSpace(source)) q.Add($"source={Uri.EscapeDataString(source)}");
            if (!string.IsNullOrWhiteSpace(pageToken)) q.Add($"pageToken={Uri.EscapeDataString(pageToken)}");
            if (q.Count > 0) url += "?" + string.Join("&", q);

            var page = await _http.GetFromJsonAsync<IonDirectoryPageDto>(url, JsonOptions, ct);
            if (page?.Items is { Count: > 0 }) items.AddRange(page.Items);
            pageToken = page?.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken) && ++safety < 200);
        return items;
    }

    public async Task<IReadOnlyList<IonFusedPricingDto>> GetFusedPricingAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        if (!Enabled) return [];
        var distinct = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0) return [];

        var all = new List<IonFusedPricingDto>();
        for (var i = 0; i < distinct.Count; i += MaxBatchKeys)
        {
            var chunk = distinct.Skip(i).Take(MaxBatchKeys).ToArray();
            using var resp = await _http.PostAsJsonAsync("api/v1/pct/customers/pricing", new { keys = chunk }, JsonOptions, ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<IonFusedPricingBatchDto>(JsonOptions, ct);
            if (body?.Customers is { Count: > 0 }) all.AddRange(body.Customers);
        }
        return all;
    }

    public async Task<IReadOnlyList<IonResellerOrdersDto>> GetResellerOrdersAsync(long accountId, CancellationToken ct = default)
    {
        if (!Enabled) return [];
        var result = await _http.GetFromJsonAsync<List<IonResellerOrdersDto>>($"api/v1/resellers/{accountId}/orders", JsonOptions, ct);
        return result ?? [];
    }
}

// --- DTOs (shapes verified against the live gateway) ---

public sealed record IonResellerDto(
    long AccountId,
    string? Name,
    string? PrimaryContactEmail,
    bool DelegatedAdminEnabled,
    DateTime? CreateTime,
    string? ResourceName);

public sealed record IonDirectoryPageDto(
    List<IonDirectoryItemDto>? Items,
    string? NextPageToken);

public sealed record IonDirectoryItemDto(
    string? TenantId,
    string? Domain,
    string? Name,
    string? Source,
    long? IonAccountId,
    string? ResellerName);

public sealed record IonFusedPricingBatchDto(
    List<IonFusedPricingDto>? Customers,
    List<string>? NotFound);

public sealed record IonFusedPricingDto(
    string? CustomerId,
    string? Domain,
    string? ResellerName,
    long? IonAccountId,
    bool ResellerMatched,
    List<IonFusedPricingLineDto>? Lines,
    DateTime? SyncedAt,
    DateTime? EnrichedAt);

public sealed record IonFusedPricingLineDto(
    string? SubscriptionId,
    string? OfferName,
    string? SkuTitle,
    string? ProductId,
    string? SkuId,
    string? CatalogItemId,
    string? SkuPartNumber,
    int Quantity,
    string? Status,
    decimal? UnitListPrice,
    string? ListPriceCurrency,
    decimal? IonPrice,
    decimal? IonCost,
    decimal? IonMsrp,
    decimal? IonUnitMargin,
    decimal? IonLineMargin,
    decimal? IonMarginPct,
    string? IonCurrency,
    string? PricingSource,
    string? MatchedOn);

public sealed record IonResellerOrdersDto(
    long AccountId,
    long CustomerId,
    string? ResellerName,
    string? ResellerEmail,
    List<IonOrderDto>? Orders);

public sealed record IonOrderDto(
    string? OrderId,
    string? DisplayName,
    string? Status,
    string? CurrencyCode,
    decimal? Total,
    decimal? Margin,
    DateTime? CreatedAt,
    List<IonOrderLineDto>? Lines);

public sealed record IonOrderLineDto(
    string? ProductId,
    string? SkuId,
    string? MfgPartNumber,
    string? ProductName,
    string? SkuName,
    string? PlanName,
    string? ProductType,
    string? Action,
    string? Status,
    int Quantity,
    decimal? Price,
    decimal? Cost,
    decimal? Msrp,
    decimal? Margin);
