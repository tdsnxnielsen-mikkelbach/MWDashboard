namespace MWDashboard.Shared.Models;

/// <summary>Input projection of an Ion Gateway directory row used to onboard/annotate a customer.</summary>
public sealed record IonDirectoryCustomer(
    string? TenantId,
    string? Name,
    string? Domain,
    string? Source,
    long? IonAccountId,
    string? ResellerName);

/// <summary>
/// A TD SYNNEX indirect reseller (partner) from the Ion Gateway master-data directory. Dimension table
/// (not a dated snapshot): upserted by <see cref="AccountId"/>. The account id is the join key to a
/// reseller's Ion order pricing and to <see cref="TenantInfo.IonAccountId"/> on its customers.
/// </summary>
public class IonReseller
{
    public int Id { get; set; }
    public long AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PrimaryContactEmail { get; set; } = string.Empty;
    public bool DelegatedAdminEnabled { get; set; }
    public DateTime? CreatedTime { get; set; }
    /// <summary>Microsoft Partner Network id, resolved from the Partner Center directory (name-matched). Empty when unknown.</summary>
    public string MpnId { get; set; } = string.Empty;
    /// <summary>Partner Center reseller tenant id (GUID), resolved from the Partner Center directory. Empty when unknown.</summary>
    public string PctResellerId { get; set; } = string.Empty;
    /// <summary>Count of customers linked to this reseller in the local directory (denormalized for display).</summary>
    public int CustomerCount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-tenant, per-subscription fused pricing from the Ion Gateway: Microsoft (PCT) list price combined
/// with Ion transacted cost/margin. Sourced from <c>POST /pct/customers/pricing</c>. Stored delete-then-
/// insert per <c>(TenantId, ReportDate)</c> so each collection replaces the day's rows.
///
/// Money fields are per-unit (per seat) except <see cref="IonLineMargin"/>. <see cref="PricingSource"/>
/// and <see cref="MatchedOn"/> qualify how the Ion side was matched (reseller-level, SKU-approximate) —
/// callers should surface them so users know when Ion pricing is present vs list-price-only.
/// </summary>
public class LicensePricingSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;
    public string OfferName { get; set; } = string.Empty;
    public string SkuTitle { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string SkuId { get; set; } = string.Empty;
    /// <summary>PCT catalog item id (<c>productId:skuId</c>) — the join key to <see cref="LicenseSnapshot"/> when no part number.</summary>
    public string CatalogItemId { get; set; } = string.Empty;
    /// <summary>Microsoft part number (e.g. <c>MICROSOFT_365_BUSINESS_STANDARD</c>) — primary join to <see cref="LicenseSnapshot.SkuPartNumber"/>.</summary>
    public string SkuPartNumber { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Status { get; set; } = string.Empty;

    // Microsoft list price (PCT)
    public decimal? UnitListPrice { get; set; }
    public string ListPriceCurrency { get; set; } = string.Empty;

    // Ion transacted price/cost/margin (per unit, except IonLineMargin)
    public decimal? IonUnitPrice { get; set; }
    public decimal? IonUnitCost { get; set; }
    public decimal? IonMsrp { get; set; }
    public decimal? IonUnitMargin { get; set; }
    public decimal? IonMarginPct { get; set; }
    public decimal? IonLineMargin { get; set; }
    public string IonCurrency { get; set; } = string.Empty;

    public string ResellerName { get; set; } = string.Empty;
    public long? IonAccountId { get; set; }
    public bool ResellerMatched { get; set; }
    /// <summary><c>ion-reseller-sku</c> when Ion pricing matched, else <c>none</c> (list price only).</summary>
    public string PricingSource { get; set; } = string.Empty;
    /// <summary><c>mfgPartNumber</c> (term-precise), <c>catalogItemId</c> (SKU-level) or empty.</summary>
    public string MatchedOn { get; set; } = string.Empty;

    public DateTime ReportDate { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Realised reseller pricing intelligence from the Ion Gateway <c>/resellers/{accountId}/orders</c> crawl:
/// the latest transacted price/cost/margin per <c>(reseller account, Ion customer, part number)</c>. Keyed
/// by Ion ids because indirect-reseller customers are not tenant-addressable in Ion — this is a distributor
/// margin view, not joinable to a specific Entra tenant. Upserted per <c>(IonAccountId, IonCustomerId,
/// MfgPartNumber, ReportDate)</c>.
/// </summary>
public class ResellerPricingSnapshot
{
    public int Id { get; set; }
    public long IonAccountId { get; set; }
    public string ResellerName { get; set; } = string.Empty;
    public long IonCustomerId { get; set; }

    public string MfgPartNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public int Quantity { get; set; }

    public decimal? UnitPrice { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? Msrp { get; set; }
    /// <summary>The order line's total margin (absolute amount = (unitPrice − unitCost) × quantity).</summary>
    public decimal? LineMargin { get; set; }
    /// <summary>Gross margin on sell price = (price − cost) / price × 100.</summary>
    public decimal? MarginPct { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string LastOrderId { get; set; } = string.Empty;
    public DateTime? LastOrderDate { get; set; }
    public string Status { get; set; } = string.Empty;

    public DateTime ReportDate { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
