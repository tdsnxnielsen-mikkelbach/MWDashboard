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

public class WorkloadActivitySnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string Workload { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public long Count { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class CopilotUsageSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string AppName { get; set; } = string.Empty;
    public int ActiveUsers { get; set; }
    public int TotalAssignedLicenses { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class UserSegmentSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public int HeavyUsers { get; set; }
    public int LightUsers { get; set; }
    public int InactiveUsers { get; set; }
    public int TotalUsers { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class DepartmentUsageSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string Department { get; set; } = string.Empty;
    public int ActiveUsers { get; set; }
    public int TotalUsers { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class StorageSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public long UsedBytes { get; set; }
    public long AllocatedBytes { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class ConsumptionSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public long StorageUsedBytes { get; set; }
    public long StorageAllocatedBytes { get; set; }
    public long TotalActivityCount { get; set; }
    public int ActiveUserCount { get; set; }
    public int LicensedUserCount { get; set; }
    public double AvgWorkloadsPerUser { get; set; }
    public double ConsumptionScore { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class M365AppUsageSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public int UserCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class SecureScoreSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public double CurrentScore { get; set; }
    public double MaxScore { get; set; }
    public int ActiveUserCount { get; set; }
    public int LicensedUserCount { get; set; }
    // Average score across all tenants (Microsoft's comparative benchmark) for the same date
    public double ComparativeScoreAllTenants { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class SecureScoreControlSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string ControlName { get; set; } = string.Empty;
    public string ControlCategory { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Score { get; set; }
    public double ScoreInPercentage { get; set; }
    public string ImplementationStatus { get; set; } = string.Empty;
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class MfaRegistrationSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    // Member users only (guests excluded) — point-in-time registration counts
    public int TotalUsers { get; set; }
    public int MfaRegistered { get; set; }
    public int MfaCapable { get; set; }
    public int PasswordlessCapable { get; set; }
    public int SsprRegistered { get; set; }
    public int SsprCapable { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class InactiveAccountSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    // Enabled, licensed member accounts only — point-in-time staleness counts
    public int TotalLicensedUsers { get; set; }
    public int Inactive30 { get; set; }   // no interactive sign-in in 30+ days (includes never)
    public int Inactive60 { get; set; }   // no interactive sign-in in 60+ days (includes never)
    public int Inactive90 { get; set; }   // no interactive sign-in in 90+ days (includes never)
    public int NeverSignedIn { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class ServiceHealthSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    // ServiceOperational, Investigating, ServiceDegradation, ServiceInterruption, RestoringService, etc.
    public string Status { get; set; } = string.Empty;
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

public class ServiceHealthIssueSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;  // advisory / incident
    public string Status { get; set; } = string.Empty;
    public string Feature { get; set; } = string.Empty;
    public DateTime? StartDateTime { get; set; }
    public bool IsResolved { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
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
