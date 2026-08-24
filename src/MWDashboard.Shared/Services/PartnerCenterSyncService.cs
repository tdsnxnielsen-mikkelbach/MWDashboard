using Microsoft.Extensions.Logging;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

/// <summary>
/// Drives enrichment from the Partner Center Transfer (PCT) API: resolves reseller MPN/PCT-id onto the
/// Ion reseller directory, then walks each reseller's customers to onboard them as tenant-addressable
/// pricing-only records and store their fused (list price + Ion cost/margin) subscription pricing.
/// No-op when PCT is not configured.
/// </summary>
public interface IPartnerCenterSyncService
{
    bool Enabled { get; }

    /// <summary>Match Partner Center resellers onto the Ion reseller directory by name, filling MPN + PCT id. Returns matches.</summary>
    Task<int> SyncResellersAsync(CancellationToken ct = default);

    /// <summary>
    /// Walk every reseller's customers via PCT, onboarding them (tenant GUID + domain + reseller) and
    /// storing each customer's fused pricing. Returns (customersOnboarded, customersPriced).
    /// </summary>
    Task<(int Onboarded, int Priced)> SyncCustomersAndPricingAsync(CancellationToken ct = default);

    /// <summary>Refresh a single tenant's fused pricing directly from PCT. Returns the number of rows written.</summary>
    Task<int> SyncTenantPricingAsync(string tenantId, string tenantName, CancellationToken ct = default);
}

public sealed class PartnerCenterSyncService : IPartnerCenterSyncService
{
    private readonly IPartnerCenterClient _pct;
    private readonly IMauDataService _data;
    private readonly ILogger<PartnerCenterSyncService> _logger;

    public PartnerCenterSyncService(IPartnerCenterClient pct, IMauDataService data, ILogger<PartnerCenterSyncService> logger)
    {
        _pct = pct;
        _data = data;
        _logger = logger;
    }

    public bool Enabled => _pct.Enabled;

    public async Task<int> SyncResellersAsync(CancellationToken ct = default)
    {
        if (!Enabled) return 0;
        var pctResellers = await _pct.GetResellersAsync(ct);
        var ionResellers = await _data.GetResellersAsync();
        var byName = ionResellers
            .GroupBy(r => Normalize(r.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var updates = new List<IonReseller>();
        foreach (var p in pctResellers)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            if (byName.TryGetValue(Normalize(p.Name), out var ion))
            {
                ion.MpnId = p.MpnId ?? ion.MpnId;
                ion.PctResellerId = p.Id ?? ion.PctResellerId;
                updates.Add(ion);
            }
        }
        if (updates.Count > 0)
            await _data.SaveResellersAsync(updates);
        _logger.LogInformation("PCT: matched {Matched} of {Total} Partner Center resellers to Ion resellers", updates.Count, pctResellers.Count);
        return updates.Count;
    }

    public async Task<(int Onboarded, int Priced)> SyncCustomersAndPricingAsync(CancellationToken ct = default)
    {
        if (!Enabled) return (0, 0);
        var pctResellers = await _pct.GetResellersAsync(ct);
        var ionResellers = await _data.GetResellersAsync();
        var accountByName = ionResellers
            .GroupBy(r => Normalize(r.Name))
            .ToDictionary(g => g.Key, g => g.First().AccountId);
        var accountByPctId = ionResellers
            .Where(r => !string.IsNullOrEmpty(r.PctResellerId))
            .ToDictionary(r => r.PctResellerId, r => r.AccountId, StringComparer.OrdinalIgnoreCase);

        var today = DateTime.UtcNow.Date;
        var onboard = new System.Collections.Concurrent.ConcurrentBag<IonDirectoryCustomer>();
        var priced = 0;

        using var gate = new SemaphoreSlim(4);
        var tasks = pctResellers.Where(r => !string.IsNullOrWhiteSpace(r.Id)).Select(async reseller =>
        {
            await gate.WaitAsync(ct);
            try
            {
                long? accountId = null;
                if (accountByPctId.TryGetValue(reseller.Id!, out var a1)) accountId = a1;
                else if (!string.IsNullOrWhiteSpace(reseller.Name) && accountByName.TryGetValue(Normalize(reseller.Name), out var a2)) accountId = a2;

                var customers = await _pct.GetResellerCustomerSubscriptionsAsync(reseller.Id!, ion: true, ct);
                foreach (var customer in customers)
                {
                    if (string.IsNullOrWhiteSpace(customer.CustomerId)) continue;
                    onboard.Add(new IonDirectoryCustomer(
                        customer.CustomerId, customer.CustomerName, customer.Domain, "Pct", accountId, reseller.Name));

                    var rows = MapPricing(customer.Subscriptions, customer.CustomerId!, customer.CustomerName ?? customer.Domain ?? customer.CustomerId!, reseller.Name, accountId);
                    if (rows.Count > 0)
                    {
                        await _data.SaveLicensePricingAsync(customer.CustomerId!, today, rows);
                        Interlocked.Increment(ref priced);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PCT: reseller-subscriptions sweep failed for reseller {ResellerId}", reseller.Id);
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);

        var (added, annotated) = await _data.UpsertIonCustomersAsync(onboard);
        _logger.LogInformation("PCT: onboarded {Added} new customers ({Annotated} annotated), priced {Priced}", added, annotated, priced);
        return (added + annotated, priced);
    }

    public async Task<int> SyncTenantPricingAsync(string tenantId, string tenantName, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(tenantId)) return 0;
        var subs = await _pct.GetCustomerPricingAsync(tenantId, ct);
        var rows = MapPricing(subs.ToList(), tenantId, tenantName, resellerName: null, ionAccountId: null);
        await _data.SaveLicensePricingAsync(tenantId, DateTime.UtcNow.Date, rows);
        return rows.Count;
    }

    private static List<LicensePricingSnapshot> MapPricing(
        List<PctSubscriptionDto>? subs, string tenantId, string tenantName, string? resellerName, long? ionAccountId)
    {
        if (subs is not { Count: > 0 }) return [];
        return subs.Select(s =>
        {
            // PCT exposes ionCost + ionUnitMargin; derive the per-unit sell price (ionPrice = cost + margin).
            decimal? ionUnitPrice = s.IonCost.HasValue && s.IonUnitMargin.HasValue ? s.IonCost + s.IonUnitMargin : null;
            return new LicensePricingSnapshot
            {
                TenantId = tenantId,
                TenantName = tenantName,
                SubscriptionId = s.Id ?? string.Empty,
                OfferName = s.OfferName ?? s.FriendlyName ?? string.Empty,
                SkuTitle = s.SkuTitle ?? string.Empty,
                ProductId = s.ProductId ?? string.Empty,
                SkuId = s.SkuId ?? string.Empty,
                CatalogItemId = s.CatalogItemId ?? string.Empty,
                SkuPartNumber = s.SkuPartNumber ?? string.Empty,
                Quantity = s.Quantity,
                Status = s.Status ?? string.Empty,
                UnitListPrice = s.UnitListPrice,
                ListPriceCurrency = s.ListPriceCurrency ?? string.Empty,
                IonUnitPrice = ionUnitPrice,
                IonUnitCost = s.IonCost,
                IonMsrp = s.IonMsrp,
                IonUnitMargin = s.IonUnitMargin,
                IonMarginPct = s.IonMarginPct,
                IonLineMargin = s.IonLineMargin,
                IonCurrency = s.IonCurrency ?? string.Empty,
                ResellerName = resellerName ?? string.Empty,
                IonAccountId = ionAccountId,
                ResellerMatched = ionAccountId.HasValue,
                PricingSource = s.IonPricingSource ?? string.Empty,
                MatchedOn = s.IonMatchedOn ?? string.Empty
            };
        })
        .GroupBy(r => r.SubscriptionId)
        .Select(g => g.First())
        .ToList();
    }

    private static string Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : new string(name.Trim().TrimEnd('*').Trim().ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
}
