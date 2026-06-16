namespace MWDashboard.Shared.Models;

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

// Per-user M365 Apps usage detail (anonymized). Sourced from the getM365AppUserDetail report.
// The user's UPN is replaced with a non-reversible, tenant-scoped pseudonym (UserKey) — no raw
// PII is ever stored. Captures which app the user used on which platform (the app x platform matrix).
public class M365AppUserDetailSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string UserKey { get; set; } = string.Empty; // anonymized user identifier (HMAC of UPN)
    public DateTime? LastActivityDate { get; set; }
    public DateTime? LastActivationDate { get; set; }

    // App x platform usage matrix (true = the user used that app on that platform).
    public bool OutlookWindows { get; set; }
    public bool WordWindows { get; set; }
    public bool ExcelWindows { get; set; }
    public bool PowerPointWindows { get; set; }
    public bool OneNoteWindows { get; set; }
    public bool TeamsWindows { get; set; }

    public bool OutlookMac { get; set; }
    public bool WordMac { get; set; }
    public bool ExcelMac { get; set; }
    public bool PowerPointMac { get; set; }
    public bool OneNoteMac { get; set; }
    public bool TeamsMac { get; set; }

    public bool OutlookMobile { get; set; }
    public bool WordMobile { get; set; }
    public bool ExcelMobile { get; set; }
    public bool PowerPointMobile { get; set; }
    public bool OneNoteMobile { get; set; }
    public bool TeamsMobile { get; set; }

    public bool OutlookWeb { get; set; }
    public bool WordWeb { get; set; }
    public bool ExcelWeb { get; set; }
    public bool PowerPointWeb { get; set; }
    public bool OneNoteWeb { get; set; }
    public bool TeamsWeb { get; set; }

    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// Aggregate Office activation counts per product type and device platform.
// Sourced from the getOffice365ActivationCounts report. Contains no PII.
public class Office365ActivationSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string ProductType { get; set; } = string.Empty;
    public int WindowsCount { get; set; }
    public int MacCount { get; set; }
    public int AndroidCount { get; set; }
    public int IosCount { get; set; }
    public int WindowsMobileCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

// Per-user Office activation detail (anonymized). Sourced from getOffice365ActivationsUserDetail.
// UPN is replaced with a tenant-scoped pseudonym (UserKey); the report's Display Name column
// (pure PII) is intentionally never stored.
public class Office365ActivationUserSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string UserKey { get; set; } = string.Empty; // anonymized user identifier (HMAC of UPN)
    public string ProductType { get; set; } = string.Empty;
    public DateTime? LastActivatedDate { get; set; }
    public bool Windows { get; set; }
    public bool Mac { get; set; }
    public bool WindowsMobile { get; set; }
    public bool Ios { get; set; }
    public bool Android { get; set; }
    public bool SharedComputer { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
