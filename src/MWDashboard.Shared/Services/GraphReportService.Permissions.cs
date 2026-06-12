using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using MWDashboard.Shared.Models;
using BetaGraphClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace MWDashboard.Shared.Services;

public partial class GraphReportService
{

    // Returns the data row with the most recent parseable date in the given column.
    private static string[]? LatestByDate(List<string[]> rows, int dateIndex)
    {
        string[]? best = null;
        var bestDate = DateTime.MinValue;
        for (int r = 1; r < rows.Count; r++)
        {
            if (DateTime.TryParse(GetValue(rows[r], dateIndex), out var d) && d >= bestDate)
            {
                bestDate = d;
                best = rows[r];
            }
        }
        return best;
    }

    /// <summary>
    /// Like <see cref="LatestByDate"/>, but skips rows where every supplied value column is empty.
    /// Graph time-series "counts" reports emit the current refresh-date row with blank columns, so
    /// the most recent row carrying data is typically one or two days behind the latest date.
    /// </summary>
    private static string[]? LatestRowWithData(List<string[]> rows, int dateIndex, params int[] valueIndexes)
    {
        string[]? best = null;
        var bestDate = DateTime.MinValue;
        for (int r = 1; r < rows.Count; r++)
        {
            var hasData = valueIndexes.Any(i => !string.IsNullOrWhiteSpace(GetValue(rows[r], i)));
            if (!hasData) continue;

            if (DateTime.TryParse(GetValue(rows[r], dateIndex), out var d) && d >= bestDate)
            {
                bestDate = d;
                best = rows[r];
            }
        }
        // Fall back to the strictly latest row if no row carried any value (preserves prior behaviour).
        return best ?? LatestByDate(rows, dateIndex);
    }

    // Quote-aware CSV parser (usage-detail reports may contain quoted fields with embedded commas).
    private static List<string[]> ParseCsv(string csv)
    {
        var result = new List<string[]>();
        foreach (var rawLine in csv.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;

            var fields = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (ch == ',' && !inQuotes)
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else sb.Append(ch);
            }
            fields.Add(sb.ToString());
            result.Add(fields.ToArray());
        }
        return result;
    }

    // Consent health — probes each required Graph application permission with a minimal call.
    // Microsoft 365 Copilot license SKU IDs (assigning any of these grants a paid Copilot seat).
    // Used to determine which Copilot-Chat audit users are licensed vs. unlicensed.
    private static readonly HashSet<Guid> CopilotSkuIds =
    [
        Guid.Parse("639dec6b-bb19-468b-871c-c5c441c4b0cb"), // Microsoft 365 Copilot
    ];

    public async Task<HashSet<string>> GetCopilotLicensedUpnsAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var upns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var page = await client.Users.GetAsync(config =>
            {
                config.QueryParameters.Select = ["userPrincipalName", "assignedLicenses"];
                config.QueryParameters.Top = 999;
            });

            if (page?.Value == null)
                return upns;

            void Accumulate(Microsoft.Graph.Models.User u)
            {
                if (string.IsNullOrEmpty(u.UserPrincipalName) || u.AssignedLicenses == null)
                    return;
                if (u.AssignedLicenses.Any(l => l.SkuId.HasValue && CopilotSkuIds.Contains(l.SkuId.Value)))
                    upns.Add(u.UserPrincipalName);
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.User, Microsoft.Graph.Models.UserCollectionResponse>
                .CreatePageIterator(client, page, u => { Accumulate(u); return true; });
            await iterator.IterateAsync();
        }
        catch (Exception ex)
        {
            // Missing license/permission/data just means we can't confirm licensing — treat all as unlicensed.
            _logger.LogWarning(ex, "Failed to resolve Copilot-licensed users for tenant {TenantId}; treating all Copilot Chat users as unlicensed", tenantId);
        }

        return upns;
    }

    // Returns the display names of permissions that are NOT consented in the target tenant
    // (i.e. the tenant admin needs to re-consent). An empty list means all permissions are present.
    public async Task<List<string>> CheckMissingPermissionsAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var missing = new List<string>();

        await ProbePermissionAsync(missing, "Organization.Read.All",
            () => client.Organization.GetAsync());
        await ProbePermissionAsync(missing, "User.Read.All",
            () => client.Users.GetAsync(c => { c.QueryParameters.Top = 1; c.QueryParameters.Select = ["id"]; }));
        await ProbePermissionAsync(missing, "Reports.Read.All",
            () => client.Reports.GetOffice365ActiveUserCountsWithPeriod("D7").GetAsync());
        await ProbePermissionAsync(missing, "ServiceMessage.Read.All",
            () => client.Admin.ServiceAnnouncement.Messages.GetAsync(c => c.QueryParameters.Top = 1));
        await ProbePermissionAsync(missing, "AuditLog.Read.All",
            () => client.Reports.AuthenticationMethods.UserRegistrationDetails.GetAsync(c => c.QueryParameters.Top = 1));
        await ProbePermissionAsync(missing, "SecurityEvents.Read.All",
            () => client.Security.SecureScores.GetAsync(c => c.QueryParameters.Top = 1));
        await ProbePermissionAsync(missing, "ServiceHealth.Read.All",
            () => client.Admin.ServiceAnnouncement.HealthOverviews.GetAsync());
        await ProbePermissionAsync(missing, "DeviceManagementManagedDevices.Read.All",
            () => client.DeviceManagement.ManagedDevices.GetAsync(c => c.QueryParameters.Top = 1));
        await ProbePermissionAsync(missing, "Policy.Read.All",
            () => client.Identity.ConditionalAccess.Policies.GetAsync(c => c.QueryParameters.Top = 1));
        await ProbePermissionAsync(missing, "Group.Read.All",
            () => client.Groups.GetAsync(c => c.QueryParameters.Top = 1));
        await ProbePermissionAsync(missing, "Application.Read.All",
            () => client.Applications.GetAsync(c => { c.QueryParameters.Top = 1; c.QueryParameters.Select = ["id"]; }));
        await ProbePermissionAsync(missing, "RoleManagement.Read.Directory",
            () => client.DirectoryRoles.GetAsync());
        // /security/alerts_v2 returns a bare 403 on tenants that aren't onboarded to Microsoft
        // Defender XDR even when SecurityAlert.Read.All IS granted, so a blanket 403 would produce
        // a false "re-consent" flag. Only flag it on a genuine authorization-denied code.
        await ProbePermissionAsync(missing, "SecurityAlert.Read.All",
            () => client.Security.Alerts_v2.GetAsync(c => c.QueryParameters.Top = 1),
            treatBare403AsConsentGap: false);
        // IdentityRiskyUser.Read.All is intentionally NOT probed here: it is Entra ID P2-gated,
        // so a 403 on a non-P2 tenant is a licensing limit, not a consent gap, and would produce
        // a false "re-consent" flag. The risky-user collection logs that case on its own.

        return missing;
    }
    private async Task ProbePermissionAsync(List<string> missing, string permission, Func<Task> probe, bool treatBare403AsConsentGap = true)
    {
        try
        {
            await probe();
        }
        catch (Exception ex)
        {
            if (IsPermissionError(ex, treatBare403AsConsentGap))
            {
                missing.Add(permission);
            }
            else
            {
                // Non-permission failures (no license, no data, throttling) don't indicate a consent gap
                _logger.LogDebug(ex, "Permission probe for {Permission} returned a non-permission error", permission);
            }
        }
    }

    private static bool IsPermissionError(Exception ex, bool treatBare403AsConsentGap = true)
    {
        if (ex is Microsoft.Graph.Models.ODataErrors.ODataError odata)
        {
            var detail = $"{odata.Error?.Code} {odata.Error?.Message} {odata.Message}";

            // A premium-license 403 (e.g. signInActivity needs Entra ID P1/P2) is NOT a consent gap —
            // the permission is granted, the tenant just isn't licensed for the data. Don't flag it.
            if (IsPremiumLicenseError(detail))
                return false;

            // An explicit authorization-denied code always means the permission is missing.
            var code = odata.Error?.Code ?? string.Empty;
            if (code.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase))
                return true;

            // A bare 403 with no auth-specific code can be a service-onboarding/licensing limit
            // rather than a consent gap (e.g. /security/alerts_v2 on tenants not onboarded to
            // Microsoft Defender XDR). Only treat it as a consent gap when the caller opts in.
            if (odata.ResponseStatusCode == 403) return treatBare403AsConsentGap;
        }

        var msg = ex.Message ?? string.Empty;
        return msg.Contains("Invalid permission", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("does not have required", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("S2SUnauthorized", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase);
    }
}
