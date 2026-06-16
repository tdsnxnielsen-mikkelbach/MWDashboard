namespace MWDashboard.Shared.Models;

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

/// <summary>
/// Daily per-(client-app × country) aggregate of interactive sign-ins, used to surface
/// <strong>legacy-authentication</strong> protocol usage (POP/IMAP/SMTP and other clients that bypass
/// modern auth / MFA) plus failed- and risky-sign-in breakdowns and a sign-in-by-country view.
/// Sourced from the Microsoft Graph beta <c>/auditLogs/signIns</c> endpoint (requires
/// <c>AuditLog.Read.All</c>) and <strong>gated behind Microsoft Entra ID P1/P2</strong> — skipped on
/// free-tier tenants. Pulled incrementally via <c>TenantInfo.SignInDetailCursorUtc</c> so history
/// accumulates beyond the tenant's ~30-day sign-in-log retention.
/// </summary>
public class SignInDetailSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    /// <summary>The client app reported by Entra, e.g. <c>Browser</c>, <c>Mobile Apps and Desktop clients</c>, <c>IMAP4</c>, <c>POP3</c>, <c>SMTP</c>, <c>Exchange ActiveSync</c>, <c>Other clients</c>.</summary>
    public string ClientApp { get; set; } = string.Empty;
    /// <summary>True when the client app is a legacy-auth protocol (anything other than <c>Browser</c> / <c>Mobile Apps and Desktop clients</c>) — these bypass modern auth and most MFA / Conditional Access.</summary>
    public bool IsLegacyAuth { get; set; }
    /// <summary>Sign-in country/region from <c>location.countryOrRegion</c> (<c>Unknown</c> when absent).</summary>
    public string Country { get; set; } = string.Empty;
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    /// <summary>Number of sign-ins whose aggregated risk level was low/medium/high (Identity Protection).</summary>
    public int RiskyCount { get; set; }
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
