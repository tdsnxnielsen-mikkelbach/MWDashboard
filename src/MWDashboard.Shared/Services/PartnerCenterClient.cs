using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MWDashboard.Shared.Services;

/// <summary>
/// Typed client for the <b>Partner Center Transfer (PCT) API</b> — the direct Microsoft Partner Center
/// CSP view. Unlike the Ion Gateway (pricing/cost), PCT is the authoritative source of the indirect
/// reseller → customer (Entra tenant GUID + domain) → subscription chain, and its subscription payload
/// is already decorated with Microsoft list price AND (with <c>ion=true</c>) TD SYNNEX StreamOne
/// cost/margin. This is what lets MWDashboard turn reseller orders into tenant-addressable, priced customers.
///
/// Auth: a shared <c>X-Api-Key</c> header (the service acquires the Partner Center token itself via the
/// Secure App Model). Config: <c>PartnerCenter:BaseUrl</c> + <c>PartnerCenter:ApiKey</c>.
/// </summary>
public interface IPartnerCenterClient
{
    bool Enabled { get; }

    /// <summary>All indirect resellers linked to the partner account (id = PCT reseller GUID, mpnId, name).</summary>
    Task<IReadOnlyList<PctResellerDto>> GetResellersAsync(CancellationToken ct = default);

    /// <summary>
    /// Every customer of a reseller with all its subscriptions in one paged sweep (25 customers/page).
    /// With <paramref name="ion"/> true each subscription carries list price + Ion cost/margin. Auto-pages.
    /// </summary>
    Task<IReadOnlyList<PctResellerCustomerDto>> GetResellerCustomerSubscriptionsAsync(
        string resellerId, bool ion = true, CancellationToken ct = default);

    /// <summary>One customer's subscriptions fused with list price + Ion cost/margin (the "cockpit" pricing view).</summary>
    Task<IReadOnlyList<PctSubscriptionDto>> GetCustomerPricingAsync(string customerId, CancellationToken ct = default);
}

public sealed class PartnerCenterClient : IPartnerCenterClient
{
    private const int PageSize = 25;

    private readonly HttpClient _http;
    private readonly ILogger<PartnerCenterClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public PartnerCenterClient(HttpClient http, IConfiguration config, ILogger<PartnerCenterClient> logger)
    {
        _http = http;
        _logger = logger;
        var baseUrl = config["PartnerCenter:BaseUrl"];
        var apiKey = config["PartnerCenter:ApiKey"];
        Enabled = !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(apiKey);
        if (Enabled)
        {
            _http.BaseAddress = new Uri(baseUrl!.TrimEnd('/') + "/");
            if (!_http.DefaultRequestHeaders.Contains("X-Api-Key"))
                _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            _http.Timeout = TimeSpan.FromMinutes(3);
        }
    }

    public bool Enabled { get; }

    public async Task<IReadOnlyList<PctResellerDto>> GetResellersAsync(CancellationToken ct = default)
    {
        if (!Enabled) return [];
        var result = await _http.GetFromJsonAsync<List<PctResellerDto>>("api/v1/resellers", JsonOptions, ct);
        return result ?? [];
    }

    public async Task<IReadOnlyList<PctResellerCustomerDto>> GetResellerCustomerSubscriptionsAsync(
        string resellerId, bool ion = true, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(resellerId)) return [];
        var all = new List<PctResellerCustomerDto>();
        var page = 1;
        while (true)
        {
            var url = $"api/v1/resellers/{Uri.EscapeDataString(resellerId)}/subscriptions?page={page}&ion={ion.ToString().ToLowerInvariant()}";
            var slice = await _http.GetFromJsonAsync<List<PctResellerCustomerDto>>(url, JsonOptions, ct);
            if (slice is not { Count: > 0 }) break;
            all.AddRange(slice);
            if (slice.Count < PageSize) break;
            if (++page > 400) break; // hard safety
        }
        return all;
    }

    public async Task<IReadOnlyList<PctSubscriptionDto>> GetCustomerPricingAsync(string customerId, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(customerId)) return [];
        var result = await _http.GetFromJsonAsync<List<PctSubscriptionDto>>(
            $"api/v1/customers/{Uri.EscapeDataString(customerId)}/subscriptions/pricing", JsonOptions, ct);
        return result ?? [];
    }
}

// --- DTOs (Partner Center Transfer API schemas) ---

public sealed record PctResellerDto(string? Id, string? MpnId, string? Name);

public sealed record PctResellerCustomerDto(
    string? CustomerId,
    string? CustomerName,
    string? Domain,
    List<PctSubscriptionDto>? Subscriptions,
    DateTime? LastSyncedAt);

public sealed record PctSubscriptionDto(
    string? Id,
    string? FriendlyName,
    string? OfferName,
    string? OfferId,
    string? SkuTitle,
    string? ProductType,
    string? Status,
    string? ProductId,
    string? SkuId,
    int Quantity,
    string? BillingCycle,
    string? TermDuration,
    bool IsTrial,
    string? CatalogItemId,
    string? SkuPartNumber,
    decimal? UnitListPrice,
    string? ListPriceCurrency,
    decimal? IonCost,
    decimal? IonMsrp,
    decimal? IonUnitMargin,
    decimal? IonMarginPct,
    decimal? IonLineMargin,
    string? IonCurrency,
    string? IonPricingSource,
    string? IonMatchedOn);
