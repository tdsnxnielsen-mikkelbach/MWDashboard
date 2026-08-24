using Microsoft.Extensions.Logging;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

/// <summary>
/// Orchestrates data sync from the TD SYNNEX Ion Gateway into MWDashboard: the reseller directory,
/// ION customer onboarding (as pricing-only tenants), per-tenant fused pricing (list price + Ion
/// cost/margin) and reseller realised-order pricing intelligence. All operations are no-ops when the
/// gateway is not configured (<see cref="IIonGatewayClient.Enabled"/> is false).
/// </summary>
public interface IIonSyncService
{
    bool Enabled { get; }

    /// <summary>Refresh the reseller directory from the gateway. Returns the number of resellers written.</summary>
    Task<int> SyncResellersAsync(CancellationToken ct = default);

    /// <summary>
    /// Import the ION/PCT customer directory, onboarding unknown tenants as pricing-only records and
    /// annotating existing ones. Returns (added, annotated).
    /// </summary>
    Task<(int Added, int Annotated)> SyncCustomersAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetch fused pricing for all ION-linked customers (in ≤500-key batches) and store it per tenant.
    /// Returns the number of tenants for which pricing rows were written.
    /// </summary>
    Task<int> SyncCustomerPricingAsync(CancellationToken ct = default);

    /// <summary>
    /// Refresh a single tenant's fused pricing (used inline during that tenant's collection run).
    /// Returns the number of pricing rows written.
    /// </summary>
    Task<int> SyncTenantPricingAsync(string tenantId, string tenantName, CancellationToken ct = default);

    /// <summary>
    /// Crawl every reseller's orders and store the latest realised price/cost/margin per SKU. Returns the
    /// number of reseller-pricing rows written.
    /// </summary>
    Task<int> SyncResellerPricingAsync(CancellationToken ct = default);
}

public sealed class IonSyncService : IIonSyncService
{
    private readonly IIonGatewayClient _client;
    private readonly IMauDataService _data;
    private readonly ILogger<IonSyncService> _logger;

    public IonSyncService(IIonGatewayClient client, IMauDataService data, ILogger<IonSyncService> logger)
    {
        _client = client;
        _data = data;
        _logger = logger;
    }

    public bool Enabled => _client.Enabled;

    public async Task<int> SyncResellersAsync(CancellationToken ct = default)
    {
        if (!Enabled) return 0;
        var resellers = await _client.GetResellersAsync(ct: ct);
        var rows = resellers.Select(r => new IonReseller
        {
            AccountId = r.AccountId,
            Name = r.Name ?? string.Empty,
            PrimaryContactEmail = r.PrimaryContactEmail ?? string.Empty,
            DelegatedAdminEnabled = r.DelegatedAdminEnabled,
            CreatedTime = r.CreateTime
        }).ToList();
        var n = await _data.SaveResellersAsync(rows);
        _logger.LogInformation("ION: synced {Count} resellers", n);
        return n;
    }

    public async Task<(int Added, int Annotated)> SyncCustomersAsync(CancellationToken ct = default)
    {
        if (!Enabled) return (0, 0);
        var directory = await _client.GetCustomerDirectoryAsync(ct: ct);
        var customers = directory
            .Where(d => !string.IsNullOrWhiteSpace(d.TenantId))
            .Select(d => new IonDirectoryCustomer(d.TenantId, d.Name, d.Domain, d.Source, d.IonAccountId, d.ResellerName));
        var result = await _data.UpsertIonCustomersAsync(customers);
        _logger.LogInformation("ION: onboarded {Added} new customers, annotated {Annotated}", result.Added, result.Annotated);
        return result;
    }

    public async Task<int> SyncCustomerPricingAsync(CancellationToken ct = default)
    {
        if (!Enabled) return 0;
        var customers = await _data.GetIonCustomersAsync();
        var keys = customers.Select(c => c.TenantId).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (keys.Count == 0) return 0;

        var names = customers.ToDictionary(c => c.TenantId, c => c.TenantName, StringComparer.OrdinalIgnoreCase);
        var fused = await _client.GetFusedPricingAsync(keys, ct);
        var today = DateTime.UtcNow.Date;
        var priced = 0;
        foreach (var customer in fused)
        {
            if (string.IsNullOrWhiteSpace(customer.CustomerId)) continue;
            var name = names.TryGetValue(customer.CustomerId, out var n) ? n : (customer.Domain ?? customer.CustomerId);
            var rows = MapPricing(customer, name);
            if (rows.Count == 0) continue;
            await _data.SaveLicensePricingAsync(customer.CustomerId, today, rows);
            priced++;
        }
        _logger.LogInformation("ION: priced {Count} customers", priced);
        return priced;
    }

    public async Task<int> SyncTenantPricingAsync(string tenantId, string tenantName, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(tenantId)) return 0;
        var fused = await _client.GetFusedPricingAsync([tenantId], ct);
        var customer = fused.FirstOrDefault();
        if (customer is null) return 0;
        var rows = MapPricing(customer, tenantName);
        await _data.SaveLicensePricingAsync(tenantId, DateTime.UtcNow.Date, rows);
        return rows.Count;
    }

    public async Task<int> SyncResellerPricingAsync(CancellationToken ct = default)
    {
        if (!Enabled) return 0;
        var resellers = await _client.GetResellersAsync(ct: ct);
        var today = DateTime.UtcNow.Date;
        var total = 0;

        // Bounded concurrency across resellers — the gateway is rate-limited (120 req/min).
        using var gate = new SemaphoreSlim(4);
        var tasks = resellers.Select(async reseller =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var orderGroups = await _client.GetResellerOrdersAsync(reseller.AccountId, ct);
                var rows = MapResellerPricing(orderGroups, today);
                if (rows.Count > 0)
                {
                    await _data.SaveResellerPricingAsync(today, rows);
                    Interlocked.Add(ref total, rows.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ION: reseller-orders crawl failed for account {AccountId}", reseller.AccountId);
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
        _logger.LogInformation("ION: wrote {Count} reseller-pricing rows", total);
        return total;
    }

    private static List<LicensePricingSnapshot> MapPricing(IonFusedPricingDto customer, string tenantName)
    {
        if (customer.Lines is not { Count: > 0 }) return [];
        return customer.Lines.Select(l => new LicensePricingSnapshot
        {
            TenantId = customer.CustomerId ?? string.Empty,
            TenantName = tenantName,
            SubscriptionId = l.SubscriptionId ?? string.Empty,
            OfferName = l.OfferName ?? string.Empty,
            SkuTitle = l.SkuTitle ?? string.Empty,
            ProductId = l.ProductId ?? string.Empty,
            SkuId = l.SkuId ?? string.Empty,
            CatalogItemId = l.CatalogItemId ?? string.Empty,
            SkuPartNumber = l.SkuPartNumber ?? string.Empty,
            Quantity = l.Quantity,
            Status = l.Status ?? string.Empty,
            UnitListPrice = l.UnitListPrice,
            ListPriceCurrency = l.ListPriceCurrency ?? string.Empty,
            IonUnitPrice = l.IonPrice,
            IonUnitCost = l.IonCost,
            IonMsrp = l.IonMsrp,
            IonUnitMargin = l.IonUnitMargin,
            IonMarginPct = l.IonMarginPct,
            IonLineMargin = l.IonLineMargin,
            IonCurrency = l.IonCurrency ?? string.Empty,
            ResellerName = customer.ResellerName ?? string.Empty,
            IonAccountId = customer.IonAccountId,
            ResellerMatched = customer.ResellerMatched,
            PricingSource = l.PricingSource ?? string.Empty,
            MatchedOn = l.MatchedOn ?? string.Empty
        })
        // De-dup lines sharing a subscription id (unique index is (TenantId, SubscriptionId, ReportDate)).
        .GroupBy(r => r.SubscriptionId)
        .Select(g => g.First())
        .ToList();
    }

    private static List<ResellerPricingSnapshot> MapResellerPricing(IReadOnlyList<IonResellerOrdersDto> orderGroups, DateTime reportDate)
    {
        var rows = new List<ResellerPricingSnapshot>();
        foreach (var group in orderGroups)
        {
            // Latest realised line per part number for this reseller customer.
            var latestByPart = new Dictionary<string, ResellerPricingSnapshot>(StringComparer.OrdinalIgnoreCase);
            var orders = (group.Orders ?? []).OrderByDescending(o => o.CreatedAt ?? DateTime.MinValue);
            foreach (var order in orders)
            {
                foreach (var line in order.Lines ?? [])
                {
                    var part = line.MfgPartNumber;
                    if (string.IsNullOrWhiteSpace(part) || latestByPart.ContainsKey(part)) continue;

                    decimal? marginPct = line.Price is > 0 && line.Cost is not null
                        ? Math.Round((line.Price.Value - line.Cost.Value) / line.Price.Value * 100m, 4)
                        : null;

                    latestByPart[part] = new ResellerPricingSnapshot
                    {
                        IonAccountId = group.AccountId,
                        ResellerName = group.ResellerName ?? string.Empty,
                        IonCustomerId = group.CustomerId,
                        MfgPartNumber = part,
                        ProductName = line.ProductName ?? string.Empty,
                        SkuName = line.SkuName ?? line.PlanName ?? string.Empty,
                        Quantity = line.Quantity,
                        UnitPrice = line.Price,
                        UnitCost = line.Cost,
                        Msrp = line.Msrp,
                        LineMargin = line.Margin,
                        MarginPct = marginPct,
                        Currency = order.CurrencyCode ?? string.Empty,
                        LastOrderId = order.OrderId ?? string.Empty,
                        LastOrderDate = order.CreatedAt,
                        Status = line.Status ?? order.Status ?? string.Empty,
                        ReportDate = reportDate
                    };
                }
            }
            rows.AddRange(latestByPart.Values);
        }
        return rows;
    }
}
