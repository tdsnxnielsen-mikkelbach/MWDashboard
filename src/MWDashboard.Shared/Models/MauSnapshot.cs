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

/// <summary>
/// Daily usage of free, unlicensed Microsoft 365 Copilot Chat (e.g. BizChat), sourced from raw
/// <c>CopilotInteraction</c> audit events in the Office 365 Management Activity API
/// (<c>Audit.General</c> content blobs) — data the Graph Copilot reports API does not expose.
/// Aggregated per surface (<see cref="AppHost"/>) per day. <see cref="UnlicensedUsers"/> is derived
/// by cross-referencing interacting users against assigned Copilot SKUs (<see cref="LicenseSnapshot"/>).
/// </summary>
public class CopilotChatUsageSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    /// <summary>The audit <c>AppHost</c> surface (e.g. <c>BizChat</c>, <c>Bing</c>, <c>Edge</c>, <c>Office</c>, <c>M365App</c>).</summary>
    public string AppHost { get; set; } = string.Empty;
    /// <summary>Distinct users who had at least one Copilot Chat interaction on this surface this day.</summary>
    public int ActiveUsers { get; set; }
    /// <summary>Total Copilot Chat interactions (events) on this surface this day.</summary>
    public int InteractionCount { get; set; }
    /// <summary>Subset of <see cref="ActiveUsers"/> who do NOT hold a Microsoft 365 Copilot license.</summary>
    public int UnlicensedUsers { get; set; }
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

// Intune device compliance — tenant-level point-in-time counts (Tier 2)
public class DeviceComplianceSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public int TotalDevices { get; set; }
    public int CompliantCount { get; set; }
    public int NonCompliantCount { get; set; }
    public int InGracePeriodCount { get; set; }
    public int ErrorCount { get; set; }
    public int UnknownCount { get; set; }
    // OS breakdown
    public int WindowsCount { get; set; }
    public int IosCount { get; set; }
    public int AndroidCount { get; set; }
    public int MacOsCount { get; set; }
    public int OtherOsCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// Conditional Access coverage — tenant-level policy counts + gap flags (Tier 2)
public class ConditionalAccessSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public int TotalPolicies { get; set; }
    public int EnabledPolicies { get; set; }
    public int ReportOnlyPolicies { get; set; }
    public int DisabledPolicies { get; set; }
    // Coverage gap flags (true = the protection is present in an enabled policy)
    public bool BlocksLegacyAuth { get; set; }
    public bool RequiresMfa { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// Guest / external users — tenant-level governance counts (Tier 2)
public class GuestUserSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public int TotalGuests { get; set; }
    public int AcceptedGuests { get; set; }
    public int PendingAcceptanceGuests { get; set; }
    public int RecentlyAddedGuests { get; set; }   // created in the last 30 days
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// Risky users (Identity Protection) — tenant-level counts, Entra ID P2 only (Tier 2)
public class RiskyUserSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public int TotalAtRisk { get; set; }   // riskState == atRisk or confirmedCompromised
    public int HighRisk { get; set; }
    public int MediumRisk { get; set; }
    public int LowRisk { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// ---- Tier 3: Usage & Governance ----

// Mailbox usage — tenant-level aggregate (getMailboxUsageDetail + getMailboxUsageQuotaStatusMailboxCounts)
public class MailboxUsageSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public int TotalMailboxes { get; set; }
    public int ActiveMailboxes { get; set; }      // had activity in the reporting window
    public int InactiveMailboxes { get; set; }    // no activity >= 30 days
    public long TotalStorageUsedBytes { get; set; }
    public int UnderLimitCount { get; set; }
    public int WarningCount { get; set; }
    public int SendProhibitedCount { get; set; }
    public int SendReceiveProhibitedCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// Top-N largest mailboxes by storage (one row per ranked mailbox per tenant per day)
public class TopMailboxSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public int Rank { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public long StorageUsedBytes { get; set; }
    public long ItemCount { get; set; }
    public DateTime? LastActivityDate { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// Teams device usage — tenant-level user counts by client/device type (getTeamsDeviceUsageUserCounts)
public class TeamsDeviceUsageSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public int WindowsCount { get; set; }
    public int MacCount { get; set; }
    public int WebCount { get; set; }
    public int IosCount { get; set; }
    public int AndroidPhoneCount { get; set; }
    public int WindowsPhoneCount { get; set; }
    public int ChromeOsCount { get; set; }
    public int LinuxCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// SharePoint / OneDrive usage — tenant-level aggregate. Workload discriminator: "SharePoint" | "OneDrive"
public class SiteUsageSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string Workload { get; set; } = string.Empty;  // SharePoint | OneDrive
    public int TotalSites { get; set; }                    // sites (SharePoint) or accounts (OneDrive)
    public int ActiveSites { get; set; }                   // had file activity in the window
    public long TotalStorageUsedBytes { get; set; }
    public long TotalFileCount { get; set; }
    public long ActiveFileCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// Top-N SharePoint sites / OneDrive accounts by storage. Workload discriminator: "SharePoint" | "OneDrive"
public class SiteUsageDetailSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string Workload { get; set; } = string.Empty;  // SharePoint | OneDrive
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;       // site URL (SharePoint) or owner (OneDrive)
    public long StorageUsedBytes { get; set; }
    public long FileCount { get; set; }
    public long ActiveFileCount { get; set; }
    public DateTime? LastActivityDate { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// Viva Engage (Yammer) activity — tenant-level user counts (getYammerActivityUserCounts)
public class YammerActivitySnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public int PostedCount { get; set; }   // users who posted
    public int ReadCount { get; set; }     // users who read
    public int LikedCount { get; set; }    // users who liked
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// Groups & Teams sprawl — tenant-level governance counts (GET /groups). Requires Group.Read.All
public class GroupSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public int TotalGroups { get; set; }
    public int M365Groups { get; set; }          // unified groups
    public int SecurityGroups { get; set; }
    public int DistributionGroups { get; set; }  // mail-enabled, non-security, non-unified (distribution lists)
    public int TeamsConnectedGroups { get; set; }
    public int OwnerlessGroups { get; set; }     // M365 groups with zero owners
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
