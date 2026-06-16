namespace MWDashboard.Shared.Models;

/// <summary>
/// One row per app-registration / service-principal secret or certificate, capturing its
/// expiry so admins can rotate credentials before they lapse. Sourced from Microsoft Graph
/// <c>/applications</c> + <c>/servicePrincipals</c> (requires <c>Application.Read.All</c>).
/// </summary>
public class AppCredentialSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    /// <summary>The application's <c>appId</c> (client id).</summary>
    public string AppId { get; set; } = string.Empty;
    /// <summary>The directory object id of the application or service principal.</summary>
    public string AppObjectId { get; set; } = string.Empty;
    public string AppDisplayName { get; set; } = string.Empty;
    /// <summary><c>Secret</c> (client secret / password) or <c>Certificate</c> (key credential).</summary>
    public string CredentialType { get; set; } = string.Empty;
    /// <summary>The credential's <c>keyId</c> (unique within the app).</summary>
    public string KeyId { get; set; } = string.Empty;
    /// <summary>Optional friendly hint/display name set on the credential.</summary>
    public string DisplayName { get; set; } = string.Empty;
    public DateTime EndDateTime { get; set; }
    /// <summary>Whole days until expiry (negative if already expired).</summary>
    public int DaysToExpiry { get; set; }
    public bool IsExpired { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row per Entra directory role per collection, capturing how many members hold each
/// privileged role. Sourced from Microsoft Graph <c>/directoryRoles</c> (requires
/// <c>RoleManagement.Read.Directory</c>).
/// </summary>
public class PrivilegedRoleSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string RoleTemplateId { get; set; } = string.Empty;
    /// <summary>Number of standing (active) members assigned to the role.</summary>
    public int MemberCount { get; set; }
    /// <summary>True for high-impact roles (e.g. Global Administrator, Privileged Role Administrator).</summary>
    public bool IsPrivileged { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row per enterprise application / service principal that holds OAuth permission grants, with
/// the high-risk scopes it was granted. Surfaces the illicit-consent / OAuth-phishing attack surface.
/// Sourced from Microsoft Graph <c>/servicePrincipals</c> + <c>/oauth2PermissionGrants</c>
/// (requires <c>Application.Read.All</c> + <c>Directory.Read.All</c>).
/// </summary>
public class OAuthGrantSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string AppDisplayName { get; set; } = string.Empty;
    /// <summary>The application's <c>appId</c> (client id).</summary>
    public string AppId { get; set; } = string.Empty;
    /// <summary><c>Delegated</c> (on-behalf-of-user) or <c>Application</c> (app-only app-role) grant.</summary>
    public string GrantType { get; set; } = string.Empty;
    /// <summary>Comma-separated list of high-risk scopes/app-roles this app was granted (e.g. <c>Mail.Read,Files.ReadWrite.All</c>).</summary>
    public string HighRiskScopes { get; set; } = string.Empty;
    /// <summary>Total number of scopes/app-roles granted to this app.</summary>
    public int ScopeCount { get; set; }
    /// <summary>True when the grant is admin-consented (tenant-wide); false for individual user consent.</summary>
    public bool IsAdminConsented { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Daily aggregate of external file/folder sharing activity by share type, sourced from the
/// Office 365 Management Activity API <c>Audit.SharePoint</c> feed (requires <c>ActivityFeed.Read</c>).
/// </summary>
public class ExternalSharingSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    /// <summary><c>Anonymous</c> (anyone-with-the-link), <c>External</c> (specific guests), or <c>Organization</c> (people-in-your-org links).</summary>
    public string ShareType { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public int DistinctUsers { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Daily aggregate of suspicious mailbox-rule / auto-forwarding activity, a classic Business Email
/// Compromise (BEC) indicator. Sourced from the Office 365 Management Activity API
/// <c>Audit.Exchange</c> feed (requires <c>ActivityFeed.Read</c>).
/// </summary>
public class MailRuleEventSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    /// <summary><c>Forwarding</c> (forward to external/SMTP address), <c>Redirect</c> (redirect-to rule), or <c>Delete</c> (auto-delete/move rule).</summary>
    public string RuleType { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public int DistinctMailboxes { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Daily aggregate of non-owner / delegate mailbox access — an insider-threat and compromised-delegate
/// indicator that pairs with the suspicious mailbox-rule detection. Sourced from the Office 365
/// Management Activity API <c>Audit.Exchange</c> feed (<c>MailItemsAccessed</c>,
/// <c>Add-MailboxPermission</c>; requires <c>ActivityFeed.Read</c>), parsed from the same content
/// blobs already pulled for mailbox-rule detection — no extra API calls.
/// </summary>
public class MailboxAccessSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    /// <summary><c>NonOwnerAccess</c> (a non-owner read mailbox items) or <c>DelegateGrant</c> (a delegate/full-access permission was added).</summary>
    public string AccessType { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public int DistinctMailboxes { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Daily aggregate of Microsoft Purview Data Loss Prevention (DLP) policy matches by policy and
/// severity, sourced from the Office 365 Management Activity API <c>DLP.All</c> feed (requires
/// <c>ActivityFeed.Read</c>). Only populated for tenants that actually run DLP policies.
/// </summary>
public class DlpEventSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    /// <summary><c>High</c>, <c>Medium</c>, <c>Low</c>, or empty when the event carries no severity.</summary>
    public string Severity { get; set; } = string.Empty;
    public int MatchCount { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Daily aggregate of Microsoft Entra directory-audit change events ("what changed, when, who did it"),
/// grouped by category + activity. Sourced from Microsoft Graph <c>/auditLogs/directoryAudits</c>
/// (requires <c>AuditLog.Read.All</c>). Because the tenant only retains audit events for ~7 days
/// (free) / ~30 days (P1/P2), each collection persists new events here so history accumulates over
/// time beyond the tenant's retention window. Actor identities are pseudonymized (no raw UPNs stored).
/// </summary>
public class DirectoryAuditSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    /// <summary>Audit category, e.g. <c>RoleManagement</c>, <c>UserManagement</c>, <c>ApplicationManagement</c>, <c>GroupManagement</c>, <c>Policy</c>.</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>The activity display name, e.g. <c>Add member to role</c>, <c>Reset user password</c>, <c>Consent to application</c>.</summary>
    public string Activity { get; set; } = string.Empty;
    public int EventCount { get; set; }
    /// <summary>Number of events whose result was failure (a brute-force / misconfiguration signal).</summary>
    public int FailureCount { get; set; }
    /// <summary>Number of distinct actors (pseudonymized) who performed this activity that day.</summary>
    public int DistinctActors { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row per directory subscription per collection, capturing renewal/expiry lifecycle dates so
/// upcoming subscription renewals can be surfaced. Sourced from Microsoft Graph
/// <c>/directory/subscriptions</c> (<c>companySubscription</c>; requires <c>Directory.Read.All</c>).
/// </summary>
public class SubscriptionSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string SkuId { get; set; } = string.Empty;
    public string SkuPartNumber { get; set; } = string.Empty;
    /// <summary>Subscription status, e.g. <c>Enabled</c>, <c>Warning</c>, <c>Suspended</c>, <c>Deleted</c>, <c>LockedOut</c>.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>True when the subscription is a trial.</summary>
    public bool IsTrial { get; set; }
    public int TotalLicenses { get; set; }
    /// <summary>The next renewal/expiry lifecycle date (UTC). Null when the subscription has no scheduled lifecycle change.</summary>
    public DateTime? NextLifecycleDateTime { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Daily per-SKU aggregate of license-assignment problems: users whose license assignment is in an
/// <c>Error</c> state (dependency/conflict) and accounts that are disabled but still hold a paid
/// license (seat waste). Sourced from Microsoft Graph <c>/users</c> (<c>licenseAssignmentStates</c>,
/// <c>accountEnabled</c>; requires <c>User.Read.All</c> + <c>Organization.Read.All</c>). A
/// reseller-facing "you're paying for seats that aren't applied" deliverable.
/// </summary>
public class LicenseAssignmentIssueSnapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string SkuPartNumber { get; set; } = string.Empty;
    /// <summary>The SKU GUID, when resolvable from the collected license snapshots.</summary>
    public string SkuId { get; set; } = string.Empty;
    /// <summary>Number of users whose assignment of this SKU is in an <c>Error</c> state.</summary>
    public int ErrorUsers { get; set; }
    /// <summary>Number of sign-in-blocked (disabled) accounts that still hold this SKU.</summary>
    public int DisabledLicensedUsers { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
