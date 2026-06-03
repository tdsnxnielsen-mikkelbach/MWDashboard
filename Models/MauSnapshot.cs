namespace MWDashboard.Models;

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
}

public class LicenseSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string SkuId { get; set; } = string.Empty;
    public string SkuPartNumber { get; set; } = string.Empty;
    public int TotalLicenses { get; set; }
    public int ConsumedLicenses { get; set; }
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

public class SecuritySignInSummary
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public int ActiveUserCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int MfaCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class TenantEntraTier
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string Tier { get; set; } = "Free"; // Free, P1, P2
    public bool HasSignInAccess { get; set; }
    public bool HasMfaDetails { get; set; }

    // Known Entra ID premium SKU part numbers
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
