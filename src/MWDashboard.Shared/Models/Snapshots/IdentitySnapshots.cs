namespace MWDashboard.Shared.Models;

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

// Device patch / OS-version hygiene — per (platform, OS version) device counts derived from the
// same managedDevices list as DeviceComplianceSnapshot (no extra Graph call) (Tier 1)
public class DevicePatchSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string OsPlatform { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public int DeviceCount { get; set; }
    // Devices whose lastSyncDateTime is older than the staleness threshold (default 30 days)
    public int StaleCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Daily per-platform aggregate of Microsoft Entra-registered/joined devices, surfacing device-hygiene
/// cleanup opportunities (registered devices that haven't signed in for 90+ days, plus disabled device
/// objects). Distinct from Intune <see cref="DeviceComplianceSnapshot"/> because it covers <em>all</em>
/// registered devices, not just managed ones. Sourced from Microsoft Graph <c>/devices</c>
/// (<c>operatingSystem</c>, <c>approximateLastSignInDateTime</c>, <c>accountEnabled</c>; readable with the
/// already-granted <c>Directory.Read.All</c>). Delete-then-insert per <c>(TenantId, ReportDate)</c>.
/// </summary>
public class StaleDeviceSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    /// <summary><c>Windows</c>, <c>iOS</c>, <c>Android</c>, <c>macOS</c>, or <c>Other</c>.</summary>
    public string OsPlatform { get; set; } = string.Empty;
    public int TotalDevices { get; set; }
    /// <summary>Devices whose <c>approximateLastSignInDateTime</c> is older than 90 days (or never signed in).</summary>
    public int Stale90Plus { get; set; }
    /// <summary>Device objects that are disabled (<c>accountEnabled == false</c>).</summary>
    public int DisabledDevices { get; set; }
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
