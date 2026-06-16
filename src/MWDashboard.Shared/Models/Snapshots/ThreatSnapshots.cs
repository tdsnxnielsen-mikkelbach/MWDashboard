namespace MWDashboard.Shared.Models;

/// <summary>
/// Daily aggregate of Microsoft Defender / Microsoft 365 security alerts by severity and status,
/// sourced from Microsoft Graph <c>/security/alerts_v2</c> (requires <c>SecurityAlert.Read.All</c>).
/// </summary>
public class DefenderAlertSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    /// <summary><c>high</c>, <c>medium</c>, <c>low</c>, or <c>informational</c>.</summary>
    public string Severity { get; set; } = string.Empty;
    /// <summary><c>new</c>, <c>inProgress</c>, or <c>resolved</c>.</summary>
    public string Status { get; set; } = string.Empty;
    public int AlertCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Daily aggregate of email threats detected/blocked by Exchange Online Protection / Microsoft Defender
/// for Office 365, classified by threat type — the "threats stopped this month" headline a reseller
/// surfaces in a QBR. Sourced from Microsoft Graph <c>/security/alerts_v2</c> filtered to email/collaboration
/// threat categories (the only aggregate email-threat signal available app-only; the Defender portal's
/// mail-flow "delivered" counts are not exposed to app-only Graph, so <see cref="DeliveredCount"/> stays 0).
/// Requires a Defender for Office 365 / EOP subscription on the tenant — gated via
/// <see cref="TenantDefenderTier"/>; the collection also no-ops gracefully on a 403. Delete-then-insert
/// per <c>(TenantId, ReportDate)</c>.
/// </summary>
public class EmailThreatSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    /// <summary><c>Malware</c>, <c>Phishing</c>, <c>Spam</c>, or <c>Other</c>.</summary>
    public string ThreatType { get; set; } = string.Empty;
    /// <summary>Number of detected/blocked email-threat alerts of this type in the reporting window.</summary>
    public int BlockedCount { get; set; }
    /// <summary>Messages that were delivered despite the threat (zero-hour auto purge gaps). Not available via app-only Graph today, so currently 0.</summary>
    public int DeliveredCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row per Attack Simulation Training campaign, capturing the security-awareness funnel (targeted →
/// clicked → reported) and the compromised rate. Sourced from Microsoft Graph
/// <c>/security/attackSimulation/simulations</c> + each campaign's <c>report/overview</c> (requires
/// <c>AttackSimulation.Read.All</c> and <strong>Defender for Office 365 Plan 2</strong>) — gated via
/// <see cref="TenantDefenderTier"/>; the collection also no-ops gracefully on a 403. No per-user PII is
/// stored (campaign-level counts only). Delete-then-insert per <c>(TenantId, ReportDate)</c>.
/// </summary>
public class AttackSimSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string CampaignName { get; set; } = string.Empty;
    /// <summary>The attack technique, e.g. <c>credentialHarvesting</c>, <c>attachmentMalware</c>, <c>linkInAttachment</c>.</summary>
    public string AttackType { get; set; } = string.Empty;
    /// <summary>Campaign status, e.g. <c>succeeded</c>, <c>running</c>, <c>scheduled</c>.</summary>
    public string Status { get; set; } = string.Empty;
    public int TargetedUsers { get; set; }
    public int ClickedCount { get; set; }
    public int ReportedCount { get; set; }
    /// <summary>Percentage of targeted users who were compromised by the simulated attack (0–100).</summary>
    public double CompromisedRate { get; set; }
    /// <summary>The campaign launch date (UTC), when available.</summary>
    public DateTime? LaunchDate { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
