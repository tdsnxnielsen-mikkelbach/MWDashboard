namespace MWDashboard.Shared.Models;

/// <summary>
/// Central lookup for the Microsoft Graph application permissions the dashboard uses.
/// Maps each scope (e.g. <c>User.Read.All</c>) to its official admin-consent display
/// name (e.g. "Read all users' full profiles") so the UI can show human-readable text
/// alongside the raw scope. Single source of truth — add new scopes here only.
/// </summary>
public static class GraphPermissions
{
    /// <summary>Scope → official Graph admin-consent display name.</summary>
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Organization.Read.All"] = "Read organization information",
        ["Directory.Read.All"] = "Read directory data",
        ["User.Read.All"] = "Read all users' full profiles",
        ["Group.Read.All"] = "Read all groups",
        ["Reports.Read.All"] = "Read all usage reports",
        ["ServiceMessage.Read.All"] = "Read service announcement messages",
        ["ServiceHealth.Read.All"] = "Read service health",
        ["AuditLog.Read.All"] = "Read all audit log data",
        ["SecurityEvents.Read.All"] = "Read your organization's security events",
        ["DeviceManagementManagedDevices.Read.All"] = "Read Microsoft Intune devices",
        ["Policy.Read.All"] = "Read your organization's policies",
        ["IdentityRiskyUser.Read.All"] = "Read all identity risky user information",
        ["Application.Read.All"] = "Read all applications",
        ["RoleManagement.Read.Directory"] = "Read all directory RBAC settings",
        ["SecurityAlert.Read.All"] = "Read all security alerts",
        ["AttackSimulation.Read.All"] = "Read attack simulation and training data of the organization",
        // Office 365 Management APIs resource (not Graph) — powers unlicensed Copilot Chat collection.
        ["ActivityFeed.Read"] = "Read activity data for your organization",
    };

    /// <summary>
    /// Permission display names for non-Graph resources used by the dashboard.
    /// </summary>
    /// <remarks>
    /// <c>ActivityFeed.Read</c> belongs to the <b>Office 365 Management APIs</b> resource (not Microsoft
    /// Graph). It powers the unlicensed Copilot Chat collection. It is granted via the same admin-consent
    /// flow (the <c>/adminconsent</c> URL grants every permission configured on the app registration), so
    /// no separate consent URL is needed — but it must be added to the app registration.
    /// It is intentionally not part of the Graph consent probe, which only exercises Graph endpoints.
    /// </remarks>
    public const string ActivityFeedRead = "ActivityFeed.Read";

    /// <summary>
    /// Returns the human-readable admin-consent display name for a Graph scope,
    /// or the scope itself if it is unknown.
    /// </summary>
    public static string Describe(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return string.Empty;
        return DisplayNames.TryGetValue(scope.Trim(), out var name) ? name : scope.Trim();
    }

    /// <summary>
    /// Formats a scope as "Display name (Scope)", e.g.
    /// "Read all users' full profiles (User.Read.All)".
    /// </summary>
    public static string DescribeWithScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return string.Empty;
        var trimmed = scope.Trim();
        return DisplayNames.TryGetValue(trimmed, out var name) ? $"{name} ({trimmed})" : trimmed;
    }
}
