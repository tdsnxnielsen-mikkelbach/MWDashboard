namespace MWDashboard.Shared.Models;

public class MauSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public int ActiveUserCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class TenantInfo
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime OnboardedAt { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// Comma-separated list of Graph permissions that failed a consent probe during the last collection.
    /// Empty means all required permissions are consented. Indicates the tenant admin must re-consent.
    /// </summary>
    public string MissingPermissions { get; set; } = string.Empty;
    public DateTime? PermissionsCheckedAt { get; set; }

    /// <summary>
    /// Cursor for the Office 365 Management Activity API Copilot-Chat collection: the
    /// <c>contentCreated</c> timestamp (UTC) of the most recent content blob already processed.
    /// Only blobs newer than this are pulled, so the 7-day retention window is never re-scanned.
    /// Null means no Copilot-Chat collection has run for this tenant yet.
    /// </summary>
    public DateTime? CopilotAuditCursorUtc { get; set; }

    /// <summary>
    /// Cursor for the Office 365 Management Activity API <c>Audit.SharePoint</c> external-sharing
    /// collection: the <c>contentCreated</c> timestamp (UTC) of the most recent content blob already
    /// processed. Only newer blobs are pulled. Null means no external-sharing collection has run yet.
    /// </summary>
    public DateTime? SharePointAuditCursorUtc { get; set; }

    /// <summary>
    /// Cursor for the Office 365 Management Activity API <c>Audit.Exchange</c> mailbox-rule
    /// collection: the <c>contentCreated</c> timestamp (UTC) of the most recent content blob already
    /// processed. Only newer blobs are pulled. Null means no mail-rule collection has run yet.
    /// </summary>
    public DateTime? ExchangeAuditCursorUtc { get; set; }

    /// <summary>
    /// Cursor for the Office 365 Management Activity API <c>DLP.All</c> collection: the
    /// <c>contentCreated</c> timestamp (UTC) of the most recent content blob already processed.
    /// Only newer blobs are pulled. Null means no DLP collection has run yet.
    /// </summary>
    public DateTime? DlpAuditCursorUtc { get; set; }

    /// <summary>
    /// Cursor for the Microsoft Graph <c>/auditLogs/directoryAudits</c> change-tracking collection:
    /// the <c>activityDateTime</c> (UTC) of the most recent directory-audit event already processed.
    /// Only events newer than this are pulled, so the tenant's short audit-retention window
    /// (~7 days on free tiers, ~30 days on P1/P2) never re-scanned — history accumulates in our DB.
    /// Null means no directory-audit collection has run yet for this tenant.
    /// </summary>
    public DateTime? DirectoryAuditCursorUtc { get; set; }

    /// <summary>
    /// Cursor for the Microsoft Graph (beta) <c>/auditLogs/signIns</c> legacy-auth / risky sign-in
    /// collection: the <c>createdDateTime</c> (UTC) of the most recent sign-in already processed.
    /// Only newer sign-ins are pulled, so the tenant's ~30-day sign-in-log retention is never
    /// re-scanned — history accumulates in our DB. Null means no sign-in-detail collection has run yet.
    /// (Entra ID P1/P2 only — skipped on free-tier tenants.)
    /// </summary>
    public DateTime? SignInDetailCursorUtc { get; set; }
}

public class LicenseSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string SkuId { get; set; } = string.Empty;
    public string SkuPartNumber { get; set; } = string.Empty;
    public int TotalLicenses { get; set; }
    public int ConsumedLicenses { get; set; }
    /// <summary>
    /// Comma-separated list of M365 services included in this SKU (auto-detected from service plans).
    /// e.g. "Teams,Exchange,SharePoint,OneDrive,Office365"
    /// </summary>
    public string IncludedServices { get; set; } = string.Empty;
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class MessageCenterPost
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? StartDateTime { get; set; }
    public DateTime? EndDateTime { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class TenantEntraTier
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string Tier { get; set; } = "Free";
    public bool HasSignInAccess { get; set; }
    public bool HasMfaDetails { get; set; }

    private static readonly string[] P2Skus = ["AAD_PREMIUM_P2", "IDENTITY_THREAT_PROTECTION", "EMSPREMIUM", "SPE_E5", "MICROSOFT_365_E5", "M365_E5"];
    private static readonly string[] P1Skus = ["AAD_PREMIUM", "EMSPREMIUM", "SPE_E3", "MICROSOFT_365_E3", "M365_E3", "M365_BUSINESS_PREMIUM"];

    public static TenantEntraTier FromLicenses(string tenantId, string tenantName, IEnumerable<string> skuPartNumbers)
    {
        var skus = skuPartNumbers.Select(s => s.ToUpperInvariant()).ToHashSet();
        var tier = new TenantEntraTier { TenantId = tenantId, TenantName = tenantName };

        if (P2Skus.Any(p => skus.Contains(p)))
        {
            tier.Tier = "P2";
            tier.HasSignInAccess = true;
            tier.HasMfaDetails = true;
        }
        else if (P1Skus.Any(p => skus.Contains(p)))
        {
            tier.Tier = "P1";
            tier.HasSignInAccess = true;
            tier.HasMfaDetails = true;
        }
        else
        {
            tier.Tier = "Free";
            tier.HasSignInAccess = false;
            tier.HasMfaDetails = false;
        }

        return tier;
    }
}

/// <summary>
/// Derives a tenant's Microsoft Defender for Office 365 capability from its license SKUs (no Graph call),
/// mirroring <see cref="TenantEntraTier"/>. Used to <em>skip</em> threat-protection collection (email
/// threats, Attack Simulation Training) on tenants that lack the required plan — a missing plan is a
/// licensing limit, not a consent gap, so it must never be surfaced as "needs re-consent".
/// </summary>
public class TenantDefenderTier
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    /// <summary><c>None</c>, <c>MDO P1</c>, or <c>MDO P2</c>.</summary>
    public string Tier { get; set; } = "None";
    /// <summary>True when the tenant has EOP/MDO email threat protection (Plan 1 or Plan 2).</summary>
    public bool HasEmailThreatProtection { get; set; }
    /// <summary>True when the tenant has Attack Simulation Training (Defender for Office 365 Plan 2 only).</summary>
    public bool HasAttackSimulation { get; set; }

    // Microsoft Defender for Office 365 Plan 2 (includes Attack Simulation Training) — standalone + bundles.
    private static readonly string[] P2Skus =
    [
        "THREAT_INTELLIGENCE", "ATP_ENTERPRISE_PREMIUM", "EOP_ENTERPRISE_PREMIUM",
        "SPE_E5", "MICROSOFT_365_E5", "M365_E5", "M365_E5_SECURITY", "MICROSOFT_365_E5_SECURITY",
        "ENTERPRISEPREMIUM", "ENTERPRISEPREMIUM_NOPSTNCONF", "IDENTITY_THREAT_PROTECTION", "DEFENDER_ENDPOINT_P2"
    ];

    // Microsoft Defender for Office 365 Plan 1 (email threat protection, no Attack Simulation) — standalone + bundles.
    private static readonly string[] P1Skus =
    [
        "ATP_ENTERPRISE", "SPB", "EOP_ENTERPRISE", "DEFENDER_FOR_OFFICE365_P1"
    ];

    public static TenantDefenderTier FromLicenses(string tenantId, string tenantName, IEnumerable<string> skuPartNumbers)
    {
        var skus = skuPartNumbers.Select(s => s.ToUpperInvariant()).ToHashSet();
        var tier = new TenantDefenderTier { TenantId = tenantId, TenantName = tenantName };

        if (P2Skus.Any(skus.Contains))
        {
            tier.Tier = "MDO P2";
            tier.HasEmailThreatProtection = true;
            tier.HasAttackSimulation = true;
        }
        else if (P1Skus.Any(skus.Contains))
        {
            tier.Tier = "MDO P1";
            tier.HasEmailThreatProtection = true;
            tier.HasAttackSimulation = false;
        }

        return tier;
    }
}

public class BrandingSettings
{
    public int Id { get; set; }
    public string? LogoBase64 { get; set; }
    public string? LogoContentType { get; set; }
    public string? FaviconBase64 { get; set; }
    public string? FaviconContentType { get; set; }
    public string LightPrimary { get; set; } = "#1976d2";
    public string LightSecondary { get; set; } = "#424242";
    public string LightAppbar { get; set; } = "#1976d2";
    public string DarkPrimary { get; set; } = "#90caf9";
    public string DarkSecondary { get; set; } = "#ce93d8";
    public string DarkAppbar { get; set; } = "#1e1e2e";
    public string? AppTitle { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
