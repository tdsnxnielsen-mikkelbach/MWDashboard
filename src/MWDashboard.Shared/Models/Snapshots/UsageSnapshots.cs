namespace MWDashboard.Shared.Models;

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

/// <summary>
/// Per-team activity detail (top-N teams by activity), sourced from Microsoft Graph
/// <c>getTeamsTeamActivityDetail</c> (requires <c>Reports.Read.All</c>). Team identities are
/// organizational (not user PII); display names follow the tenant's report privacy setting.
/// </summary>
public class TeamsTeamActivitySnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string TeamId { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    /// <summary><c>Public</c>, <c>Private</c>, etc.</summary>
    public string TeamType { get; set; } = string.Empty;
    public int ActiveUsers { get; set; }
    public int ActiveChannels { get; set; }
    public int Guests { get; set; }
    public int ChannelMessages { get; set; }
    public int ReplyMessages { get; set; }
    public int MeetingsOrganized { get; set; }
    public int Reactions { get; set; }
    public DateTime? LastActivityDate { get; set; }
    public int Rank { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
