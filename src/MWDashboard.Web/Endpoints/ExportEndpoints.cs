using System.Globalization;
using System.IO.Compression;
using System.Text;
using MWDashboard.Shared.Services;

namespace MWDashboard.Web.Endpoints;

/// <summary>
/// CSV export endpoints (one per dataset) plus an "export all" ZIP. All endpoints enforce
/// the same tenant-data isolation as the UI: home-tenant users export all tenants, customer-tenant
/// users only ever export their own tenant — derived server-side from the auth claims,
/// never from client input.
/// </summary>
public static class ExportEndpoints
{
    private const string TenantIdClaim = "http://schemas.microsoft.com/identity/claims/tenantid";

    /// <summary>A named CSV dataset: its download filename, header row, and async row builder.</summary>
    private sealed record CsvExport(
        string FileName,
        string Header,
        Func<IMauDataService, IEnumerable<string>?, Task<IEnumerable<string>>> BuildRows);

    /// <summary>Single source of truth for every exportable dataset, keyed by its URL feature name.</summary>
    private static readonly Dictionary<string, CsvExport> Exports = new(StringComparer.OrdinalIgnoreCase)
    {
        ["consumption"] = new("consumption-report.csv",
            "TenantId,TenantName,ReportDate,ConsumptionScore,ActiveUsers,LicensedUsers,AdoptionPct,StorageUsedGB,AvgWorkloads,TotalActivity",
            async (data, scope) => (await data.GetConsumptionAsync(scope, months: 12)).Select(c =>
            {
                var adoptionPct = c.LicensedUserCount > 0 ? (double)c.ActiveUserCount / c.LicensedUserCount * 100 : 0;
                return Join(F(c.TenantId), F(c.TenantName), D(c.ReportDate), N(c.ConsumptionScore),
                    c.ActiveUserCount, c.LicensedUserCount, N(adoptionPct),
                    N(c.StorageUsedBytes / 1073741824.0), N(c.AvgWorkloadsPerUser), c.TotalActivityCount);
            })),

        ["mau"] = new("active-users-report.csv",
            "TenantId,TenantName,ReportDate,ServiceName,ActiveUserCount",
            async (data, scope) => (await data.GetMauHistoryAsync(scope, months: 12)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.ServiceName), r.ActiveUserCount))),

        ["licenses"] = new("licenses-report.csv",
            "TenantId,SkuPartNumber,SkuId,TotalLicenses,ConsumedLicenses,UtilizationPct,IncludedServices,CollectedAt",
            async (data, scope) => (await data.GetLatestLicensesAsync(scope)).Select(r =>
            {
                var util = r.TotalLicenses > 0 ? (double)r.ConsumedLicenses / r.TotalLicenses * 100 : 0;
                return Join(F(r.TenantId), F(r.SkuPartNumber), F(r.SkuId), r.TotalLicenses, r.ConsumedLicenses,
                    N(util), F(r.IncludedServices), D(r.CollectedAt));
            })),

        ["security"] = new("security-signins-report.csv",
            "TenantId,ServiceName,ReportDate,ActiveUserCount,SuccessCount,FailureCount,MfaCount",
            async (data, scope) => (await data.GetSecuritySummaryAsync(scope, days: 30)).Select(r =>
                Join(F(r.TenantId), F(r.ServiceName), D(r.ReportDate), r.ActiveUserCount,
                    r.SuccessCount, r.FailureCount, r.MfaCount))),

        ["activity"] = new("feature-usage-report.csv",
            "TenantId,TenantName,ReportDate,Workload,ActivityType,Count",
            async (data, scope) => (await data.GetWorkloadActivityAsync(scope, days: 30)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.Workload), F(r.ActivityType), r.Count))),

        ["copilot"] = new("copilot-report.csv",
            "TenantId,TenantName,ReportDate,AppName,ActiveUsers,TotalAssignedLicenses",
            async (data, scope) => (await data.GetCopilotUsageAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.AppName), r.ActiveUsers, r.TotalAssignedLicenses))),

        ["departments"] = new("departments-report.csv",
            "TenantId,TenantName,ReportDate,Department,ActiveUsers,TotalUsers",
            async (data, scope) => (await data.GetDepartmentUsageAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.Department), r.ActiveUsers, r.TotalUsers))),

        ["segmentation"] = new("segmentation-report.csv",
            "TenantId,TenantName,ReportDate,HeavyUsers,LightUsers,InactiveUsers,TotalUsers",
            async (data, scope) => (await data.GetUserSegmentsAsync(scope, months: 6)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), r.HeavyUsers, r.LightUsers, r.InactiveUsers, r.TotalUsers))),

        ["m365apps"] = new("m365-apps-report.csv",
            "TenantId,TenantName,ReportDate,AppName,Platform,UserCount",
            async (data, scope) => (await data.GetM365AppUsageAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.AppName), F(r.Platform), r.UserCount))),

        ["m365app-userdetail"] = new("m365-app-user-detail-report.csv",
            "TenantId,TenantName,ReportDate,UserKey,LastActivityDate,LastActivationDate," +
            "OutlookWindows,WordWindows,ExcelWindows,PowerPointWindows,OneNoteWindows,TeamsWindows," +
            "OutlookMac,WordMac,ExcelMac,PowerPointMac,OneNoteMac,TeamsMac," +
            "OutlookMobile,WordMobile,ExcelMobile,PowerPointMobile,OneNoteMobile,TeamsMobile," +
            "OutlookWeb,WordWeb,ExcelWeb,PowerPointWeb,OneNoteWeb,TeamsWeb",
            async (data, scope) => (await data.GetM365AppUserDetailAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.UserKey),
                    r.LastActivityDate.HasValue ? D(r.LastActivityDate.Value) : "",
                    r.LastActivationDate.HasValue ? D(r.LastActivationDate.Value) : "",
                    r.OutlookWindows, r.WordWindows, r.ExcelWindows, r.PowerPointWindows, r.OneNoteWindows, r.TeamsWindows,
                    r.OutlookMac, r.WordMac, r.ExcelMac, r.PowerPointMac, r.OneNoteMac, r.TeamsMac,
                    r.OutlookMobile, r.WordMobile, r.ExcelMobile, r.PowerPointMobile, r.OneNoteMobile, r.TeamsMobile,
                    r.OutlookWeb, r.WordWeb, r.ExcelWeb, r.PowerPointWeb, r.OneNoteWeb, r.TeamsWeb))),

        ["office-activations"] = new("office-activations-report.csv",
            "TenantId,TenantName,ReportDate,ProductType,Windows,Mac,Android,iOS,WindowsMobile",
            async (data, scope) => (await data.GetOffice365ActivationsAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.ProductType),
                    r.WindowsCount, r.MacCount, r.AndroidCount, r.IosCount, r.WindowsMobileCount))),

        ["office-activation-users"] = new("office-activation-users-report.csv",
            "TenantId,TenantName,ReportDate,UserKey,ProductType,LastActivatedDate,Windows,Mac,WindowsMobile,iOS,Android,SharedComputer",
            async (data, scope) => (await data.GetOffice365ActivationUsersAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.UserKey), F(r.ProductType),
                    r.LastActivatedDate.HasValue ? D(r.LastActivatedDate.Value) : "",
                    r.Windows, r.Mac, r.WindowsMobile, r.Ios, r.Android, r.SharedComputer))),

        ["device-compliance"] = new("device-compliance-report.csv",
            "TenantId,TenantName,ReportDate,TotalDevices,Compliant,NonCompliant,InGracePeriod,Error,Unknown,Windows,iOS,Android,macOS,Other",
            async (data, scope) => (await data.GetDeviceComplianceAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), r.TotalDevices, r.CompliantCount, r.NonCompliantCount,
                    r.InGracePeriodCount, r.ErrorCount, r.UnknownCount, r.WindowsCount, r.IosCount, r.AndroidCount, r.MacOsCount, r.OtherOsCount))),

        ["device-patch"] = new("device-patch-report.csv",
            "TenantId,TenantName,ReportDate,OsPlatform,OsVersion,DeviceCount,StaleCount",
            async (data, scope) => (await data.GetDevicePatchAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.OsPlatform), F(r.OsVersion), r.DeviceCount, r.StaleCount))),

        ["conditional-access"] = new("conditional-access-report.csv",
            "TenantId,TenantName,ReportDate,TotalPolicies,Enabled,ReportOnly,Disabled,BlocksLegacyAuth,RequiresMfa",
            async (data, scope) => (await data.GetConditionalAccessAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), r.TotalPolicies, r.EnabledPolicies, r.ReportOnlyPolicies,
                    r.DisabledPolicies, r.BlocksLegacyAuth, r.RequiresMfa))),

        ["guests"] = new("guest-users-report.csv",
            "TenantId,TenantName,ReportDate,TotalGuests,Accepted,PendingAcceptance,RecentlyAdded",
            async (data, scope) => (await data.GetGuestUsersAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), r.TotalGuests, r.AcceptedGuests, r.PendingAcceptanceGuests, r.RecentlyAddedGuests))),

        ["risky-users"] = new("risky-users-report.csv",
            "TenantId,TenantName,ReportDate,TotalAtRisk,High,Medium,Low",
            async (data, scope) => (await data.GetRiskyUsersAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), r.TotalAtRisk, r.HighRisk, r.MediumRisk, r.LowRisk))),

        ["secure-scores"] = new("secure-scores-report.csv",
            "TenantId,TenantName,ReportDate,CurrentScore,MaxScore,ActiveUsers,LicensedUsers,ComparativeScoreAllTenants",
            async (data, scope) => (await data.GetSecureScoresAsync(scope, days: 90)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), N(r.CurrentScore), N(r.MaxScore),
                    r.ActiveUserCount, r.LicensedUserCount, N(r.ComparativeScoreAllTenants)))),

        ["secure-score-controls"] = new("secure-score-controls-report.csv",
            "TenantId,TenantName,ReportDate,ControlName,Category,Description,Score,ScorePct,ImplementationStatus",
            async (data, scope) => (await data.GetSecureScoreControlsAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.ControlName), F(r.ControlCategory),
                    F(r.Description), N(r.Score), N(r.ScoreInPercentage), F(r.ImplementationStatus)))),

        ["service-health"] = new("service-health-report.csv",
            "TenantId,TenantName,ReportDate,ServiceName,Status",
            async (data, scope) => (await data.GetServiceHealthAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.ServiceName), F(r.Status)))),

        ["service-health-issues"] = new("service-health-issues-report.csv",
            "TenantId,TenantName,ReportDate,IssueId,Title,ServiceName,Classification,Status,Feature,StartDateTime,IsResolved",
            async (data, scope) => (await data.GetServiceHealthIssuesAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.IssueId), F(r.Title), F(r.ServiceName),
                    F(r.Classification), F(r.Status), F(r.Feature), DN(r.StartDateTime), r.IsResolved))),

        ["mailbox-usage"] = new("mailbox-usage-report.csv",
            "TenantId,TenantName,ReportDate,TotalMailboxes,Active,Inactive,TotalStorageBytes,UnderLimit,Warning,SendProhibited,SendReceiveProhibited",
            async (data, scope) => (await data.GetMailboxUsageAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), r.TotalMailboxes, r.ActiveMailboxes, r.InactiveMailboxes,
                    r.TotalStorageUsedBytes, r.UnderLimitCount, r.WarningCount, r.SendProhibitedCount, r.SendReceiveProhibitedCount))),

        ["top-mailboxes"] = new("top-mailboxes-report.csv",
            "TenantId,TenantName,ReportDate,Rank,DisplayName,StorageUsedBytes,ItemCount,LastActivityDate",
            async (data, scope) => (await data.GetTopMailboxesAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), r.Rank, F(r.DisplayName), r.StorageUsedBytes, r.ItemCount, DN(r.LastActivityDate)))),

        ["teams-devices"] = new("teams-devices-report.csv",
            "TenantId,TenantName,ReportDate,Windows,Mac,Web,iOS,AndroidPhone,WindowsPhone,ChromeOS,Linux",
            async (data, scope) => (await data.GetTeamsDeviceUsageAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), r.WindowsCount, r.MacCount, r.WebCount, r.IosCount,
                    r.AndroidPhoneCount, r.WindowsPhoneCount, r.ChromeOsCount, r.LinuxCount))),

        ["site-usage"] = new("site-usage-report.csv",
            "TenantId,TenantName,ReportDate,Workload,TotalSites,ActiveSites,TotalStorageBytes,TotalFileCount,ActiveFileCount",
            async (data, scope) => (await data.GetSiteUsageAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.Workload), r.TotalSites, r.ActiveSites,
                    r.TotalStorageUsedBytes, r.TotalFileCount, r.ActiveFileCount))),

        ["site-usage-detail"] = new("site-usage-detail-report.csv",
            "TenantId,TenantName,ReportDate,Workload,Rank,Name,StorageUsedBytes,FileCount,ActiveFileCount,LastActivityDate",
            async (data, scope) => (await data.GetSiteUsageDetailAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.Workload), r.Rank, F(r.Name),
                    r.StorageUsedBytes, r.FileCount, r.ActiveFileCount, DN(r.LastActivityDate)))),

        ["yammer"] = new("viva-engage-report.csv",
            "TenantId,TenantName,ReportDate,Posted,Read,Liked",
            async (data, scope) => (await data.GetYammerActivityAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), r.PostedCount, r.ReadCount, r.LikedCount))),

        ["groups"] = new("groups-report.csv",
            "TenantId,TenantName,ReportDate,TotalGroups,M365Groups,SecurityGroups,DistributionGroups,TeamsConnected,Ownerless",
            async (data, scope) => (await data.GetGroupSprawlAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), r.TotalGroups, r.M365Groups, r.SecurityGroups,
                    r.DistributionGroups, r.TeamsConnectedGroups, r.OwnerlessGroups))),

        ["app-credential-expiry"] = new("app-credential-expiry-report.csv",
            "TenantId,TenantName,ReportDate,AppDisplayName,AppId,CredentialType,KeyId,DisplayName,EndDateTime,DaysToExpiry,IsExpired",
            async (data, scope) => (await data.GetAppCredentialsAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.AppDisplayName), F(r.AppId),
                    F(r.CredentialType), F(r.KeyId), F(r.DisplayName), D(r.EndDateTime), r.DaysToExpiry, r.IsExpired))),

        ["external-sharing"] = new("external-sharing-report.csv",
            "TenantId,TenantName,ReportDate,ShareType,EventCount,DistinctUsers",
            async (data, scope) => (await data.GetExternalSharingAsync(scope, days: 30)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.ShareType), r.EventCount, r.DistinctUsers))),

        ["privileged-roles"] = new("privileged-roles-report.csv",
            "TenantId,TenantName,ReportDate,RoleName,RoleTemplateId,MemberCount,IsPrivileged",
            async (data, scope) => (await data.GetPrivilegedRolesAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.RoleName), F(r.RoleTemplateId),
                    r.MemberCount, r.IsPrivileged))),

        ["defender-alerts"] = new("defender-alerts-report.csv",
            "TenantId,TenantName,ReportDate,Severity,Status,AlertCount",
            async (data, scope) => (await data.GetDefenderAlertsAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.Severity), F(r.Status), r.AlertCount))),

        ["mail-rules"] = new("suspicious-mail-rules-report.csv",
            "TenantId,TenantName,ReportDate,RuleType,EventCount,DistinctMailboxes",
            async (data, scope) => (await data.GetMailRuleEventsAsync(scope, days: 30)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.RuleType), r.EventCount, r.DistinctMailboxes))),

        ["dlp-events"] = new("dlp-policy-matches-report.csv",
            "TenantId,TenantName,ReportDate,PolicyName,Severity,MatchCount",
            async (data, scope) => (await data.GetDlpEventsAsync(scope, days: 30)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.PolicyName), F(r.Severity), r.MatchCount))),

        ["subscriptions"] = new("subscription-renewals-report.csv",
            "TenantId,TenantName,ReportDate,SkuPartNumber,SkuId,Status,IsTrial,TotalLicenses,NextLifecycleDate",
            async (data, scope) => (await data.GetSubscriptionsAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.SkuPartNumber), F(r.SkuId),
                    F(r.Status), r.IsTrial, r.TotalLicenses, DN(r.NextLifecycleDateTime)))),

        ["teams-activity"] = new("teams-team-activity-report.csv",
            "TenantId,TenantName,ReportDate,Rank,TeamName,TeamType,ActiveUsers,ActiveChannels,Guests,ChannelMessages,ReplyMessages,MeetingsOrganized,Reactions,LastActivityDate",
            async (data, scope) => (await data.GetTeamsTeamActivityAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), r.Rank, F(r.TeamName), F(r.TeamType),
                    r.ActiveUsers, r.ActiveChannels, r.Guests, r.ChannelMessages, r.ReplyMessages,
                    r.MeetingsOrganized, r.Reactions, DN(r.LastActivityDate)))),

        ["directory-audit"] = new("directory-audit-report.csv",
            "TenantId,TenantName,ReportDate,Category,Activity,EventCount,FailureCount,DistinctActors",
            async (data, scope) => (await data.GetDirectoryAuditsAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.Category), F(r.Activity),
                    r.EventCount, r.FailureCount, r.DistinctActors))),

        ["license-issues"] = new("license-issues-report.csv",
            "TenantId,TenantName,ReportDate,SkuPartNumber,SkuId,ErrorUsers,DisabledLicensedUsers",
            async (data, scope) => (await data.GetLicenseAssignmentIssuesAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.SkuPartNumber), F(r.SkuId),
                    r.ErrorUsers, r.DisabledLicensedUsers))),

        ["oauth-grants"] = new("oauth-grants-report.csv",
            "TenantId,TenantName,ReportDate,AppDisplayName,AppId,GrantType,ScopeCount,HighRiskScopes,IsAdminConsented",
            async (data, scope) => (await data.GetOAuthGrantsAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.AppDisplayName), F(r.AppId),
                    F(r.GrantType), r.ScopeCount, F(r.HighRiskScopes), r.IsAdminConsented))),

        ["mailbox-access"] = new("mailbox-access-report.csv",
            "TenantId,TenantName,ReportDate,AccessType,EventCount,DistinctMailboxes",
            async (data, scope) => (await data.GetMailboxAccessAsync(scope)).Select(r =>
                Join(F(r.TenantId), F(r.TenantName), D(r.ReportDate), F(r.AccessType),
                    r.EventCount, r.DistinctMailboxes))),
    };

    public static void MapExportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/export").RequireAuthorization();

        // Single-dataset CSV: /api/export/{feature}
        group.MapGet("{feature}", async (string feature, IMauDataService data, HttpContext ctx, IConfiguration cfg) =>
        {
            if (!Exports.TryGetValue(feature, out var export))
                return Results.NotFound();

            var scope = ResolveScope(ctx, cfg);
            var rows = await export.BuildRows(data, scope);
            await WriteCsvAsync(ctx, export.FileName, export.Header, rows);
            return Results.Empty;
        });

        // All datasets bundled into a single ZIP: /api/export-all
        app.MapGet("/api/export-all", async (IMauDataService data, HttpContext ctx, IConfiguration cfg) =>
        {
            var scope = ResolveScope(ctx, cfg);

            ctx.Response.ContentType = "application/zip";
            ctx.Response.Headers.Append("Content-Disposition",
                $"attachment; filename=mwdashboard-export-{DateTime.UtcNow:yyyy-MM-dd}.zip");

            using var archive = new ZipArchive(ctx.Response.Body, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var export in Exports.Values)
            {
                var rows = await export.BuildRows(data, scope);
                var entry = archive.CreateEntry(export.FileName, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                await writer.WriteLineAsync(export.Header);
                foreach (var line in rows)
                    await writer.WriteLineAsync(line);
            }
        }).RequireAuthorization();
    }

    /// <summary>
    /// Resolves the tenant scope from the authenticated user's claims, mirroring
    /// MainLayout's logic. Home-tenant users get null (all tenants); customer-tenant
    /// users are restricted to their own tenant. Never trusts client-supplied input.
    /// </summary>
    private static IEnumerable<string>? ResolveScope(HttpContext ctx, IConfiguration cfg)
    {
        var userTenantId = ctx.User.FindFirst(TenantIdClaim)?.Value;
        var homeTenantId = cfg["AzureAd:TenantId"];

        if (!string.IsNullOrEmpty(userTenantId) &&
            !userTenantId.Equals(homeTenantId, StringComparison.OrdinalIgnoreCase))
        {
            return [userTenantId];
        }

        return null; // home tenant user — unrestricted
    }

    private static async Task WriteCsvAsync(HttpContext ctx, string fileName, string header, IEnumerable<string> lines)
    {
        ctx.Response.ContentType = "text/csv";
        ctx.Response.Headers.Append("Content-Disposition", $"attachment; filename={fileName}");

        await using var writer = new StreamWriter(ctx.Response.Body, Encoding.UTF8);
        await writer.WriteLineAsync(header);
        foreach (var line in lines)
            await writer.WriteLineAsync(line);
    }

    private static string Join(params object[] fields) => string.Join(',', fields);

    // CSV field escaping
    private static string F(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string D(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string DN(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string N(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
}
