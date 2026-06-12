using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using MWDashboard.Shared.Models;
using BetaGraphClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace MWDashboard.Shared.Services;

public interface IGraphReportService
{
    Task<List<MauSnapshot>> GetActiveUserCountsAsync(string tenantId, int periodDays = 180);
    Task<List<LicenseSnapshot>> GetSubscribedSkusAsync(string tenantId);
    Task<List<MessageCenterPost>> GetMessageCenterPostsAsync(string tenantId);
    Task<List<SecuritySignInSummary>> GetSignInSummaryAsync(string tenantId, int days = 30);
    Task<List<WorkloadActivitySnapshot>> GetWorkloadActivityAsync(string tenantId, int periodDays = 30);
    Task<List<CopilotUsageSnapshot>> GetCopilotUsageAsync(string tenantId);
    Task<List<UserSegmentSnapshot>> GetUserSegmentationAsync(string tenantId);
    Task<List<DepartmentUsageSnapshot>> GetDepartmentUsageAsync(string tenantId);
    Task<List<StorageSnapshot>> GetStorageUsageAsync(string tenantId);
    Task<List<M365AppUsageSnapshot>> GetM365AppUsageAsync(string tenantId);
    Task<(List<SecureScoreSnapshot> Scores, List<SecureScoreControlSnapshot> Controls)> GetSecureScoreAsync(string tenantId);
    Task<MfaRegistrationSnapshot?> GetMfaRegistrationAsync(string tenantId);
    Task<InactiveAccountSnapshot?> GetInactiveAccountsAsync(string tenantId);
    Task<(List<ServiceHealthSnapshot> Services, List<ServiceHealthIssueSnapshot> Issues)> GetServiceHealthAsync(string tenantId);
    Task<DeviceComplianceSnapshot?> GetDeviceComplianceAsync(string tenantId);
    Task<ConditionalAccessSnapshot?> GetConditionalAccessAsync(string tenantId);
    Task<GuestUserSnapshot?> GetGuestUsersAsync(string tenantId);
    Task<RiskyUserSnapshot?> GetRiskyUsersAsync(string tenantId);
    Task<(MailboxUsageSnapshot? Aggregate, List<TopMailboxSnapshot> Top)> GetMailboxUsageAsync(string tenantId);
    Task<TeamsDeviceUsageSnapshot?> GetTeamsDeviceUsageAsync(string tenantId);
    Task<(List<SiteUsageSnapshot> Aggregates, List<SiteUsageDetailSnapshot> Details)> GetSiteUsageAsync(string tenantId);
    Task<YammerActivitySnapshot?> GetYammerActivityAsync(string tenantId);
    Task<GroupSnapshot?> GetGroupSprawlAsync(string tenantId);
    Task<List<string>> CheckMissingPermissionsAsync(string tenantId);

    /// <summary>
    /// Returns the set of user principal names (lower-cased) that hold an assigned Microsoft 365
    /// Copilot license. Used to split Copilot-Chat audit activity into licensed vs. unlicensed.
    /// Returns an empty set if the tenant has no Copilot SKU (so all chat users are unlicensed).
    /// </summary>
    Task<HashSet<string>> GetCopilotLicensedUpnsAsync(string tenantId);
}

public class GraphReportService : IGraphReportService
{
    private readonly IConfiguration _config;
    private readonly ILogger<GraphReportService> _logger;

    public GraphReportService(IConfiguration config, ILogger<GraphReportService> logger)
    {
        _config = config;
        _logger = logger;
    }

    private GraphServiceClient CreateClientForTenant(string tenantId)
    {
        var credential = new ClientSecretCredential(
            tenantId,
            _config["AzureAd:ClientId"],
            _config["AzureAd:ClientSecret"]);

        return new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
    }

    public async Task<List<MauSnapshot>> GetActiveUserCountsAsync(string tenantId, int periodDays = 180)
    {
        var client = CreateClientForTenant(tenantId);
        var snapshots = new List<MauSnapshot>();
        var period = $"D{periodDays}";

        try
        {
            var report = await client.Reports
                .GetOffice365ActiveUserCountsWithPeriod(period)
                .GetAsync();

            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                snapshots.AddRange(ParseActiveUserCounts(csv, tenantId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active user counts for tenant {TenantId}", tenantId);
        }

        return snapshots;
    }

    public async Task<List<LicenseSnapshot>> GetSubscribedSkusAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var licenses = new List<LicenseSnapshot>();

        try
        {
            var skus = await client.SubscribedSkus.GetAsync();
            if (skus?.Value != null)
            {
                foreach (var sku in skus.Value)
                {
                    // Auto-detect which M365 services this SKU includes from its service plans
                    var servicePlanNames = sku.ServicePlans?
                        .Where(sp => sp.ServicePlanName != null)
                        .Select(sp => sp.ServicePlanName!)
                        .ToList() ?? [];

                    var includedServices = M365Services.DetectServicesFromPlans(servicePlanNames);

                    licenses.Add(new LicenseSnapshot
                    {
                        TenantId = tenantId,
                        SkuId = sku.SkuId?.ToString() ?? string.Empty,
                        SkuPartNumber = sku.SkuPartNumber ?? string.Empty,
                        TotalLicenses = sku.PrepaidUnits?.Enabled ?? 0,
                        ConsumedLicenses = sku.ConsumedUnits ?? 0,
                        IncludedServices = includedServices,
                        CollectedAt = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscribed SKUs for tenant {TenantId}", tenantId);
        }

        return licenses;
    }

    public async Task<List<MessageCenterPost>> GetMessageCenterPostsAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var posts = new List<MessageCenterPost>();

        try
        {
            var messages = await client.Admin.ServiceAnnouncement.Messages.GetAsync(config =>
            {
                config.QueryParameters.Top = 100;
                config.QueryParameters.Orderby = ["startDateTime desc"];
            });

            if (messages?.Value != null)
            {
                foreach (var msg in messages.Value)
                {
                    posts.Add(new MessageCenterPost
                    {
                        TenantId = tenantId,
                        MessageId = msg.Id ?? string.Empty,
                        Title = msg.Title ?? string.Empty,
                        Category = msg.Category?.ToString() ?? string.Empty,
                        Severity = msg.Severity?.ToString() ?? string.Empty,
                        Description = msg.Body?.Content ?? string.Empty,
                        StartDateTime = msg.StartDateTime?.UtcDateTime,
                        EndDateTime = msg.EndDateTime?.UtcDateTime,
                        CollectedAt = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
        {
            _logger.LogWarning("Message Center unavailable for tenant {TenantId}: {Error}. " +
                "Ensure ServiceMessage.Read.All permission is granted and admin consent completed.",
                tenantId, odataEx.Error?.Message ?? odataEx.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Message Center posts for tenant {TenantId}", tenantId);
        }

        return posts;
    }

    public async Task<List<SecuritySignInSummary>> GetSignInSummaryAsync(string tenantId, int days = 30)
    {
        var summaries = new List<SecuritySignInSummary>();

        try
        {
            var credential = new ClientSecretCredential(
                tenantId,
                _config["AzureAd:ClientId"],
                _config["AzureAd:ClientSecret"]);

            var betaClient = new BetaGraphClient(credential, ["https://graph.microsoft.com/.default"]);

            var fromDate = DateTime.UtcNow.AddDays(-days);
            var filter = $"createdDateTime ge {fromDate:yyyy-MM-ddTHH:mm:ssZ}";

            var signIns = await betaClient.AuditLogs.SignIns.GetAsync(config =>
            {
                config.QueryParameters.Filter = filter;
                config.QueryParameters.Top = 999;
                config.QueryParameters.Select = ["createdDateTime", "appDisplayName", "status", "conditionalAccessStatus", "authenticationDetails", "mfaDetail"];
            });

            if (signIns?.Value != null)
            {
                var grouped = signIns.Value
                    .Where(s => s.CreatedDateTime.HasValue)
                    .GroupBy(s => new
                    {
                        Date = s.CreatedDateTime!.Value.Date,
                        App = ClassifySecurityService(s.AppDisplayName ?? "Other")
                    });

                foreach (var group in grouped)
                {
                    var items = group.ToList();
                    summaries.Add(new SecuritySignInSummary
                    {
                        TenantId = tenantId,
                        ServiceName = group.Key.App,
                        ReportDate = group.Key.Date,
                        ActiveUserCount = items.Count,
                        SuccessCount = items.Count(s => s.Status?.ErrorCode == 0),
                        FailureCount = items.Count(s => s.Status?.ErrorCode != 0),
                        MfaCount = items.Count(s =>
                            s.AuthenticationDetails != null &&
                            s.AuthenticationDetails.Any(d =>
                                d.Succeeded == true &&
                                !string.IsNullOrEmpty(d.AuthenticationMethod) &&
                                !d.AuthenticationMethod.Equals("Password", StringComparison.OrdinalIgnoreCase))),
                        CollectedAt = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sign-in summary for tenant {TenantId} (Beta API). " +
                             "Ensure tenant has Entra ID P1/P2 and AuditLog.Read.All permission is granted.", tenantId);
        }

        return summaries;
    }

    private static string ClassifySecurityService(string appDisplayName)
    {
        var name = appDisplayName.ToLowerInvariant();
        if (name.Contains("defender")) return SecurityServices.Defender;
        if (name.Contains("conditional access") || name.Contains("entra")) return SecurityServices.EntraId;
        if (name.Contains("intune")) return SecurityServices.Intune;
        if (name.Contains("sentinel")) return SecurityServices.Sentinel;
        return SecurityServices.Other;
    }

    private static List<MauSnapshot> ParseActiveUserCounts(string csv, string tenantId)
    {
        var snapshots = new List<MauSnapshot>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2) return snapshots;

        var headers = lines[0].Split(',').Select(h => h.Trim('\r', ' ', '\uFEFF')).ToArray();
        var dateIndex = Array.IndexOf(headers, "Report Date");

        var serviceColumns = new Dictionary<string, int>
        {
            [M365Services.Office365] = Array.IndexOf(headers, "Office 365"),
            [M365Services.Teams] = Array.IndexOf(headers, "Teams"),
            [M365Services.Exchange] = Array.IndexOf(headers, "Exchange"),
            [M365Services.SharePoint] = Array.IndexOf(headers, "SharePoint"),
            [M365Services.OneDrive] = Array.IndexOf(headers, "OneDrive")
        };

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            if (values.Length <= dateIndex) continue;

            if (!DateTime.TryParse(values[dateIndex], out var reportDate)) continue;

            foreach (var (service, colIndex) in serviceColumns)
            {
                if (colIndex < 0 || colIndex >= values.Length) continue;
                if (!int.TryParse(values[colIndex], out var count)) continue;

                snapshots.Add(new MauSnapshot
                {
                    TenantId = tenantId,
                    ServiceName = service,
                    ReportDate = reportDate,
                    ActiveUserCount = count,
                    CollectedAt = DateTime.UtcNow
                });
            }
        }

        return snapshots;
    }

    public async Task<List<WorkloadActivitySnapshot>> GetWorkloadActivityAsync(string tenantId, int periodDays = 30)
    {
        var client = CreateClientForTenant(tenantId);
        var activities = new List<WorkloadActivitySnapshot>();
        var period = $"D{periodDays}";

        // Teams activity
        try
        {
            var report = await client.Reports
                .GetTeamsUserActivityCountsWithPeriod(period)
                .GetAsync();
            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                var parsed = ParseTeamsActivity(csv, tenantId);
                if (parsed.Count == 0)
                    _logger.LogWarning("Teams activity CSV returned {Lines} lines but parsed 0 activities for tenant {TenantId}. First 200 chars: {Csv}",
                        csv.Split('\n').Length, tenantId, csv.Length > 200 ? csv[..200] : csv);
                activities.AddRange(parsed);
            }
            else
            {
                _logger.LogWarning("Teams activity report returned null for tenant {TenantId}", tenantId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Teams activity for tenant {TenantId}", tenantId);
        }

        // SharePoint activity
        try
        {
            var report = await client.Reports
                .GetSharePointActivityUserCountsWithPeriod(period)
                .GetAsync();
            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                activities.AddRange(ParseSharePointActivity(csv, tenantId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get SharePoint activity for tenant {TenantId}", tenantId);
        }

        // OneDrive activity
        try
        {
            var report = await client.Reports
                .GetOneDriveActivityUserCountsWithPeriod(period)
                .GetAsync();
            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                activities.AddRange(ParseOneDriveActivity(csv, tenantId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get OneDrive activity for tenant {TenantId}", tenantId);
        }

        // Exchange activity
        try
        {
            var report = await client.Reports
                .GetEmailActivityCountsWithPeriod(period)
                .GetAsync();
            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                activities.AddRange(ParseExchangeActivity(csv, tenantId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Exchange activity for tenant {TenantId}", tenantId);
        }

        return activities;
    }

    public async Task<List<CopilotUsageSnapshot>> GetCopilotUsageAsync(string tenantId)
    {
        var snapshots = new List<CopilotUsageSnapshot>();

        try
        {
            var credential = new ClientSecretCredential(
                tenantId,
                _config["AzureAd:ClientId"],
                _config["AzureAd:ClientSecret"]);

            var betaClient = new BetaGraphClient(credential, ["https://graph.microsoft.com/.default"]);

            var report = await betaClient.Reports
                .GetMicrosoft365CopilotUsageUserDetailWithPeriod("D30")
                .GetAsync();

            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                snapshots.AddRange(ParseCopilotUsage(csv, tenantId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Copilot usage for tenant {TenantId}. " +
                "Tenant may not have Copilot licenses or Reports.Read.All permission.", tenantId);
        }

        return snapshots;
    }

    public async Task<List<UserSegmentSnapshot>> GetUserSegmentationAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var segments = new List<UserSegmentSnapshot>();

        try
        {
            var report = await client.Reports
                .GetOffice365ActiveUserDetailWithPeriod("D30")
                .GetAsync();

            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                var segment = ParseUserSegmentation(csv, tenantId);
                if (segment != null)
                    segments.Add(segment);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get user detail for segmentation for tenant {TenantId}", tenantId);
        }

        return segments;
    }

    public async Task<List<DepartmentUsageSnapshot>> GetDepartmentUsageAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var snapshots = new List<DepartmentUsageSnapshot>();

        try
        {
            // Get users with department info
            var users = await client.Users.GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "department", "assignedLicenses"];
                config.QueryParameters.Top = 999;
                config.QueryParameters.Filter = "accountEnabled eq true";
            });

            if (users?.Value == null) return snapshots;

            // Get active user detail
            var report = await client.Reports
                .GetOffice365ActiveUserDetailWithPeriod("D30")
                .GetAsync();

            if (report == null) return snapshots;

            using var reader = new StreamReader(report);
            var csv = await reader.ReadToEndAsync();
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length < 2) return snapshots;

            var headers = lines[0].Split(',');
            var upnIndex = Array.IndexOf(headers, "User Principal Name");
            var hasActivityIndex = Array.IndexOf(headers, "Has Any Activity");

            // Build lookup of active UPNs
            var activeUpns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                var values = lines[i].Split(',');
                if (hasActivityIndex >= 0 && hasActivityIndex < values.Length &&
                    upnIndex >= 0 && upnIndex < values.Length &&
                    values[hasActivityIndex].Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase))
                {
                    activeUpns.Add(values[upnIndex].Trim());
                }
            }

            // Group users by department
            var deptGroups = users.Value
                .Where(u => !string.IsNullOrWhiteSpace(u.Department))
                .GroupBy(u => u.Department!);

            var today = DateTime.UtcNow.Date;
            foreach (var group in deptGroups)
            {
                var totalInDept = group.Count();
                var activeInDept = group.Count(u =>
                    u.Mail != null && activeUpns.Contains(u.Mail) ||
                    u.UserPrincipalName != null && activeUpns.Contains(u.UserPrincipalName));

                snapshots.Add(new DepartmentUsageSnapshot
                {
                    TenantId = tenantId,
                    ReportDate = today,
                    Department = group.Key,
                    ActiveUsers = activeInDept,
                    TotalUsers = totalInDept,
                    CollectedAt = DateTime.UtcNow
                });
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (
            odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Department usage unavailable for tenant {TenantId}: insufficient permissions. " +
                "Requires User.Read.All.", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get department usage for tenant {TenantId}", tenantId);
        }

        return snapshots;
    }

    private static List<WorkloadActivitySnapshot> ParseTeamsActivity(string csv, string tenantId)
    {
        var activities = new List<WorkloadActivitySnapshot>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return activities;

        var headers = lines[0].Split(',').Select(h => h.Trim('\r', ' ', '\uFEFF')).ToArray();
        var dateIndex = Array.IndexOf(headers, "Report Date");
        var meetingsIndex = Array.IndexOf(headers, "Meetings");
        var chatIndex = Array.IndexOf(headers, "Private Chat Messages");
        var channelIndex = Array.IndexOf(headers, "Post Messages");
        var callsIndex = Array.IndexOf(headers, "Calls");

        // Fallback: if "Meetings" matches "Meetings Organized" due to IndexOf, find exact
        if (meetingsIndex >= 0 && headers[meetingsIndex] != "Meetings")
            meetingsIndex = Array.FindIndex(headers, h => h == "Meetings");

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            if (!DateTime.TryParse(GetValue(values, dateIndex), out var date)) continue;

            AddActivity(activities, tenantId, date, M365Services.Teams, ActivityTypes.TeamsMeetings, GetValue(values, meetingsIndex));
            AddActivity(activities, tenantId, date, M365Services.Teams, ActivityTypes.TeamsChatMessages, GetValue(values, chatIndex));
            AddActivity(activities, tenantId, date, M365Services.Teams, ActivityTypes.TeamsChannelMessages, GetValue(values, channelIndex));
            AddActivity(activities, tenantId, date, M365Services.Teams, ActivityTypes.TeamsCalls, GetValue(values, callsIndex));
        }

        return activities;
    }

    private static List<WorkloadActivitySnapshot> ParseSharePointActivity(string csv, string tenantId)
    {
        var activities = new List<WorkloadActivitySnapshot>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return activities;

        var headers = lines[0].Split(',').Select(h => h.Trim('\r', ' ', '\uFEFF')).ToArray();
        var dateIndex = Array.IndexOf(headers, "Report Date");
        var viewedIndex = Array.IndexOf(headers, "Viewed Or Edited");
        var sharedIntIndex = Array.IndexOf(headers, "Shared Internally");
        var sharedExtIndex = Array.IndexOf(headers, "Shared Externally");
        var syncedIndex = Array.IndexOf(headers, "Synced");
        var pageViewIndex = Array.IndexOf(headers, "Visited Page");

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            if (!DateTime.TryParse(GetValue(values, dateIndex), out var date)) continue;

            AddActivity(activities, tenantId, date, M365Services.SharePoint, ActivityTypes.SharePointFilesViewed, GetValue(values, viewedIndex));
            var sharedInt = long.TryParse(GetValue(values, sharedIntIndex), out var si) ? si : 0;
            var sharedExt = long.TryParse(GetValue(values, sharedExtIndex), out var se) ? se : 0;
            if (sharedInt + sharedExt > 0)
            {
                activities.Add(new WorkloadActivitySnapshot
                {
                    TenantId = tenantId,
                    ReportDate = date,
                    Workload = M365Services.SharePoint,
                    ActivityType = ActivityTypes.SharePointFilesShared,
                    Count = sharedInt + sharedExt,
                    CollectedAt = DateTime.UtcNow
                });
            }
            AddActivity(activities, tenantId, date, M365Services.SharePoint, ActivityTypes.SharePointPageViews, GetValue(values, pageViewIndex));
            AddActivity(activities, tenantId, date, M365Services.SharePoint, ActivityTypes.SharePointFileSynced, GetValue(values, syncedIndex));
        }

        return activities;
    }

    private static List<WorkloadActivitySnapshot> ParseOneDriveActivity(string csv, string tenantId)
    {
        var activities = new List<WorkloadActivitySnapshot>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return activities;

        var headers = lines[0].Split(',').Select(h => h.Trim('\r', ' ', '\uFEFF')).ToArray();
        var dateIndex = Array.IndexOf(headers, "Report Date");
        var viewedIndex = Array.IndexOf(headers, "Viewed Or Edited");
        var sharedIntIndex = Array.IndexOf(headers, "Shared Internally");
        var sharedExtIndex = Array.IndexOf(headers, "Shared Externally");
        var syncedIndex = Array.IndexOf(headers, "Synced");

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            if (!DateTime.TryParse(GetValue(values, dateIndex), out var date)) continue;

            AddActivity(activities, tenantId, date, M365Services.OneDrive, ActivityTypes.OneDriveFilesViewed, GetValue(values, viewedIndex));
            var sharedInt = long.TryParse(GetValue(values, sharedIntIndex), out var si) ? si : 0;
            var sharedExt = long.TryParse(GetValue(values, sharedExtIndex), out var se) ? se : 0;
            if (sharedInt + sharedExt > 0)
            {
                activities.Add(new WorkloadActivitySnapshot
                {
                    TenantId = tenantId,
                    ReportDate = date,
                    Workload = M365Services.OneDrive,
                    ActivityType = ActivityTypes.OneDriveFilesShared,
                    Count = sharedInt + sharedExt,
                    CollectedAt = DateTime.UtcNow
                });
            }
            AddActivity(activities, tenantId, date, M365Services.OneDrive, ActivityTypes.OneDriveFilesSynced, GetValue(values, syncedIndex));
        }

        return activities;
    }

    private static List<WorkloadActivitySnapshot> ParseExchangeActivity(string csv, string tenantId)
    {
        var activities = new List<WorkloadActivitySnapshot>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return activities;

        var headers = lines[0].Split(',').Select(h => h.Trim('\r', ' ', '\uFEFF')).ToArray();
        var dateIndex = Array.IndexOf(headers, "Report Date");
        var sentIndex = Array.IndexOf(headers, "Send");
        var receivedIndex = Array.IndexOf(headers, "Receive");
        var readIndex = Array.IndexOf(headers, "Read");

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            if (!DateTime.TryParse(GetValue(values, dateIndex), out var date)) continue;

            AddActivity(activities, tenantId, date, M365Services.Exchange, ActivityTypes.ExchangeEmailsSent, GetValue(values, sentIndex));
            AddActivity(activities, tenantId, date, M365Services.Exchange, ActivityTypes.ExchangeEmailsReceived, GetValue(values, receivedIndex));
            AddActivity(activities, tenantId, date, M365Services.Exchange, ActivityTypes.ExchangeEmailsRead, GetValue(values, readIndex));
        }

        return activities;
    }

    private static List<CopilotUsageSnapshot> ParseCopilotUsage(string csv, string tenantId)
    {
        var snapshots = new List<CopilotUsageSnapshot>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return snapshots;

        var headers = lines[0].Split(',');
        var dateIndex = Array.IndexOf(headers, "Report Refresh Date");

        // Count active users per app from user detail rows
        var appUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var copilotAppColumns = new Dictionary<string, int>
        {
            ["Word"] = Array.IndexOf(headers, "Microsoft 365 Copilot In Word (Active)"),
            ["Excel"] = Array.IndexOf(headers, "Microsoft 365 Copilot In Excel (Active)"),
            ["PowerPoint"] = Array.IndexOf(headers, "Microsoft 365 Copilot In PowerPoint (Active)"),
            ["Outlook"] = Array.IndexOf(headers, "Microsoft 365 Copilot In Outlook (Active)"),
            ["Teams"] = Array.IndexOf(headers, "Microsoft 365 Copilot In Teams (Active)"),
            ["OneNote"] = Array.IndexOf(headers, "Microsoft 365 Copilot In OneNote (Active)"),
            ["Loop"] = Array.IndexOf(headers, "Microsoft 365 Copilot In Loop (Active)"),
            ["Copilot Chat"] = Array.IndexOf(headers, "Microsoft 365 Copilot Chat (Active)")
        };

        DateTime reportDate = DateTime.UtcNow.Date;
        foreach (var (app, _) in copilotAppColumns)
            appUsage[app] = 0;

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            if (i == 1 && dateIndex >= 0 && dateIndex < values.Length)
                DateTime.TryParse(values[dateIndex], out reportDate);

            foreach (var (app, colIndex) in copilotAppColumns)
            {
                if (colIndex >= 0 && colIndex < values.Length &&
                    values[colIndex].Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase))
                {
                    appUsage[app]++;
                }
            }
        }

        var totalUsers = lines.Length - 1; // minus header
        foreach (var (app, activeCount) in appUsage)
        {
            snapshots.Add(new CopilotUsageSnapshot
            {
                TenantId = tenantId,
                ReportDate = reportDate,
                AppName = app,
                ActiveUsers = activeCount,
                TotalAssignedLicenses = totalUsers,
                CollectedAt = DateTime.UtcNow
            });
        }

        return snapshots;
    }

    private static UserSegmentSnapshot? ParseUserSegmentation(string csv, string tenantId)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return null;

        var headers = lines[0].Split(',');

        // Find columns indicating service activity
        var serviceActivityCols = new List<int>();
        var teamsIdx = Array.IndexOf(headers, "Has Teams License");
        var exchangeIdx = Array.IndexOf(headers, "Has Exchange License");
        var spIdx = Array.IndexOf(headers, "Has SharePoint License");
        var odIdx = Array.IndexOf(headers, "Has OneDrive License");

        // Activity columns
        var teamsActivityIdx = Array.IndexOf(headers, "Teams Last Activity Date");
        var exchangeActivityIdx = Array.IndexOf(headers, "Exchange Last Activity Date");
        var spActivityIdx = Array.IndexOf(headers, "SharePoint Last Activity Date");
        var odActivityIdx = Array.IndexOf(headers, "OneDrive Last Activity Date");

        var activityCols = new[] { teamsActivityIdx, exchangeActivityIdx, spActivityIdx, odActivityIdx }
            .Where(i => i >= 0).ToArray();

        int heavy = 0, light = 0, inactive = 0, total = 0;
        var threshold = DateTime.UtcNow.AddDays(-30);

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            total++;

            var activeWorkloads = 0;
            foreach (var colIdx in activityCols)
            {
                if (colIdx < values.Length && DateTime.TryParse(values[colIdx], out var lastActivity) && lastActivity >= threshold)
                    activeWorkloads++;
            }

            if (activeWorkloads >= 3) heavy++;
            else if (activeWorkloads >= 1) light++;
            else inactive++;
        }

        return new UserSegmentSnapshot
        {
            TenantId = tenantId,
            ReportDate = DateTime.UtcNow.Date,
            HeavyUsers = heavy,
            LightUsers = light,
            InactiveUsers = inactive,
            TotalUsers = total,
            CollectedAt = DateTime.UtcNow
        };
    }

    private static void AddActivity(List<WorkloadActivitySnapshot> list, string tenantId, DateTime date, string workload, string activityType, string? value)
    {
        if (long.TryParse(value, out var count) && count > 0)
        {
            list.Add(new WorkloadActivitySnapshot
            {
                TenantId = tenantId,
                ReportDate = date,
                Workload = workload,
                ActivityType = activityType,
                Count = count,
                CollectedAt = DateTime.UtcNow
            });
        }
    }

    private static string? GetValue(string[] values, int index)
    {
        return index >= 0 && index < values.Length ? values[index].Trim() : null;
    }

    public async Task<List<StorageSnapshot>> GetStorageUsageAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var snapshots = new List<StorageSnapshot>();

        // SharePoint storage
        try
        {
            var report = await client.Reports
                .GetSharePointSiteUsageStorageWithPeriod("D30")
                .GetAsync();
            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                snapshots.AddRange(ParseStorageReport(csv, tenantId, M365Services.SharePoint));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get SharePoint storage for tenant {TenantId}", tenantId);
        }

        // OneDrive storage
        try
        {
            var report = await client.Reports
                .GetOneDriveUsageStorageWithPeriod("D30")
                .GetAsync();
            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                snapshots.AddRange(ParseStorageReport(csv, tenantId, M365Services.OneDrive));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get OneDrive storage for tenant {TenantId}", tenantId);
        }

        // Exchange mailbox storage
        try
        {
            var report = await client.Reports
                .GetMailboxUsageStorageWithPeriod("D30")
                .GetAsync();
            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                snapshots.AddRange(ParseStorageReport(csv, tenantId, M365Services.Exchange));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Exchange mailbox storage for tenant {TenantId}", tenantId);
        }

        return snapshots;
    }

    private static List<StorageSnapshot> ParseStorageReport(string csv, string tenantId, string serviceName)
    {
        var snapshots = new List<StorageSnapshot>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return snapshots;

        var headers = lines[0].Split(',');
        var dateIndex = Array.IndexOf(headers, "Report Date");
        var usedIndex = Array.IndexOf(headers, "Storage Used (Byte)");

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            if (!DateTime.TryParse(GetValue(values, dateIndex), out var date)) continue;
            if (!long.TryParse(GetValue(values, usedIndex), out var usedBytes)) continue;

            snapshots.Add(new StorageSnapshot
            {
                TenantId = tenantId,
                ServiceName = serviceName,
                ReportDate = date,
                UsedBytes = usedBytes,
                CollectedAt = DateTime.UtcNow
            });
        }

        return snapshots;
    }

    public async Task<List<M365AppUsageSnapshot>> GetM365AppUsageAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var snapshots = new List<M365AppUsageSnapshot>();

        try
        {
            var report = await client.Reports
                .GetM365AppUserCountsWithPeriod("D30")
                .GetAsync();

            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                snapshots.AddRange(ParseM365AppUsage(csv, tenantId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get M365 App usage for tenant {TenantId}", tenantId);
        }

        return snapshots;
    }

    private static List<M365AppUsageSnapshot> ParseM365AppUsage(string csv, string tenantId)
    {
        var snapshots = new List<M365AppUsageSnapshot>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return snapshots;

        var headers = lines[0].Split(',');
        var dateIndex = Array.IndexOf(headers, "Report Date");

        // Platform columns — each contains user counts for that platform
        var platformColumns = new Dictionary<string, int>
        {
            ["Windows"] = Array.IndexOf(headers, "Windows"),
            ["Mac"] = Array.IndexOf(headers, "Mac"),
            ["Mobile"] = Array.IndexOf(headers, "Mobile"),
            ["Web"] = Array.IndexOf(headers, "Web")
        };

        // App columns
        var appColumns = new Dictionary<string, int>
        {
            ["Outlook"] = Array.IndexOf(headers, "Outlook"),
            ["Word"] = Array.IndexOf(headers, "Word"),
            ["Excel"] = Array.IndexOf(headers, "Excel"),
            ["PowerPoint"] = Array.IndexOf(headers, "PowerPoint"),
            ["OneNote"] = Array.IndexOf(headers, "OneNote"),
            ["Teams"] = Array.IndexOf(headers, "Teams")
        };

        // This endpoint returns per-date rows with app-level user counts
        // Try to parse app counts (the format varies — handle both patterns)
        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            if (!DateTime.TryParse(GetValue(values, dateIndex), out var date)) continue;

            // Try app columns first
            foreach (var (app, colIndex) in appColumns)
            {
                if (colIndex < 0 || colIndex >= values.Length) continue;
                // Keep the app even when the count is 0/blank so every app (e.g. PowerPoint)
                // always appears on the M365 Apps page rather than silently disappearing.
                int.TryParse(values[colIndex].Trim(), out var count);

                snapshots.Add(new M365AppUsageSnapshot
                {
                    TenantId = tenantId,
                    ReportDate = date,
                    AppName = app,
                    Platform = "All",
                    UserCount = count,
                    CollectedAt = DateTime.UtcNow
                });
            }

            // Platform columns
            foreach (var (platform, colIndex) in platformColumns)
            {
                if (colIndex < 0 || colIndex >= values.Length) continue;
                if (!int.TryParse(values[colIndex].Trim(), out var count) || count == 0) continue;

                snapshots.Add(new M365AppUsageSnapshot
                {
                    TenantId = tenantId,
                    ReportDate = date,
                    AppName = "All",
                    Platform = platform,
                    UserCount = count,
                    CollectedAt = DateTime.UtcNow
                });
            }
        }

        return snapshots;
    }

    // Microsoft Secure Score — daily tenant score trend + per-control remediation actions
    public async Task<(List<SecureScoreSnapshot> Scores, List<SecureScoreControlSnapshot> Controls)> GetSecureScoreAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var scores = new List<SecureScoreSnapshot>();
        var controls = new List<SecureScoreControlSnapshot>();

        try
        {
            // Returns up to 90 days of daily scores, sorted latest-first
            var result = await client.Security.SecureScores.GetAsync(config =>
            {
                config.QueryParameters.Top = 90;
            });

            var dailyScores = result?.Value;
            if (dailyScores == null || dailyScores.Count == 0)
                return (scores, controls);

            foreach (var s in dailyScores)
            {
                if (!s.CreatedDateTime.HasValue) continue;

                var comparative = s.AverageComparativeScores?
                    .FirstOrDefault(a => string.Equals(a.Basis, "AllTenants", StringComparison.OrdinalIgnoreCase))?
                    .AverageScore ?? 0;

                scores.Add(new SecureScoreSnapshot
                {
                    TenantId = tenantId,
                    ReportDate = s.CreatedDateTime.Value.UtcDateTime.Date,
                    CurrentScore = s.CurrentScore ?? 0,
                    MaxScore = s.MaxScore ?? 0,
                    ActiveUserCount = s.ActiveUserCount ?? 0,
                    LicensedUserCount = s.LicensedUserCount ?? 0,
                    ComparativeScoreAllTenants = comparative,
                    CollectedAt = DateTime.UtcNow
                });
            }

            // Per-control remediation data comes from the most recent score
            var latest = dailyScores
                .Where(s => s.CreatedDateTime.HasValue)
                .OrderByDescending(s => s.CreatedDateTime!.Value)
                .FirstOrDefault();

            if (latest?.ControlScores != null && latest.CreatedDateTime.HasValue)
            {
                var reportDate = latest.CreatedDateTime.Value.UtcDateTime.Date;
                foreach (var c in latest.ControlScores)
                {
                    if (string.IsNullOrEmpty(c.ControlName)) continue;

                    // scoreInPercentage and implementationStatus are returned in AdditionalData
                    double scorePct = 0;
                    string implStatus = string.Empty;
                    if (c.AdditionalData != null)
                    {
                        if (c.AdditionalData.TryGetValue("scoreInPercentage", out var pctObj) && pctObj != null)
                            double.TryParse(pctObj.ToString(), out scorePct);
                        if (c.AdditionalData.TryGetValue("implementationStatus", out var statusObj) && statusObj != null)
                            implStatus = statusObj.ToString() ?? string.Empty;
                    }

                    controls.Add(new SecureScoreControlSnapshot
                    {
                        TenantId = tenantId,
                        ReportDate = reportDate,
                        ControlName = c.ControlName,
                        ControlCategory = c.ControlCategory ?? string.Empty,
                        Description = c.Description ?? string.Empty,
                        Score = c.Score ?? 0,
                        ScoreInPercentage = scorePct,
                        ImplementationStatus = implStatus,
                        CollectedAt = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Secure Score for tenant {TenantId}", tenantId);
        }

        return (scores, controls);
    }

    // MFA / authentication method registration — aggregated tenant-level counts (member users only, no PII stored)
    public async Task<MfaRegistrationSnapshot?> GetMfaRegistrationAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.Reports.AuthenticationMethods.UserRegistrationDetails.GetAsync(config =>
            {
                config.QueryParameters.Top = 999;
            });

            if (page?.Value == null)
                return null;

            var snapshot = new MfaRegistrationSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            void Accumulate(Microsoft.Graph.Models.UserRegistrationDetails d)
            {
                // Count member accounts only — guests aren't part of the org's MFA posture
                if (!string.Equals(d.UserType?.ToString(), "member", StringComparison.OrdinalIgnoreCase))
                    return;

                snapshot.TotalUsers++;
                if (d.IsMfaRegistered == true) snapshot.MfaRegistered++;
                if (d.IsMfaCapable == true) snapshot.MfaCapable++;
                if (d.IsPasswordlessCapable == true) snapshot.PasswordlessCapable++;
                if (d.IsSsprRegistered == true) snapshot.SsprRegistered++;
                if (d.IsSsprCapable == true) snapshot.SsprCapable++;
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.UserRegistrationDetails, Microsoft.Graph.Models.UserRegistrationDetailsCollectionResponse>
                .CreatePageIterator(client, page, d => { Accumulate(d); return true; });
            await iterator.IterateAsync();

            return snapshot.TotalUsers > 0 ? snapshot : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get MFA registration details for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // Inactive / stale licensed accounts — tenant-level staleness counts based on last interactive sign-in
    public async Task<InactiveAccountSnapshot?> GetInactiveAccountsAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.Users.GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "accountEnabled", "userType", "assignedLicenses", "signInActivity"];
                config.QueryParameters.Top = 999;
            });

            if (page?.Value == null)
                return null;

            var snapshot = new InactiveAccountSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            var now = DateTimeOffset.UtcNow;

            void Accumulate(Microsoft.Graph.Models.User u)
            {
                // Only enabled, licensed member accounts count toward license-waste analysis
                if (u.AccountEnabled != true)
                    return;
                if (!string.Equals(u.UserType, "Member", StringComparison.OrdinalIgnoreCase))
                    return;
                if (u.AssignedLicenses == null || u.AssignedLicenses.Count == 0)
                    return;

                snapshot.TotalLicensedUsers++;

                var lastSignIn = u.SignInActivity?.LastSignInDateTime;
                if (lastSignIn == null)
                {
                    snapshot.NeverSignedIn++;
                    snapshot.Inactive30++;
                    snapshot.Inactive60++;
                    snapshot.Inactive90++;
                    return;
                }

                var daysInactive = (now - lastSignIn.Value).TotalDays;
                if (daysInactive >= 30) snapshot.Inactive30++;
                if (daysInactive >= 60) snapshot.Inactive60++;
                if (daysInactive >= 90) snapshot.Inactive90++;
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.User, Microsoft.Graph.Models.UserCollectionResponse>
                .CreatePageIterator(client, page, u => { Accumulate(u); return true; });
            await iterator.IterateAsync();

            return snapshot.TotalLicensedUsers > 0 ? snapshot : null;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            // The signInActivity property is gated behind a Microsoft Entra ID P1/P2 license.
            // A tenant on the free tier returns 403 even when AuditLog.Read.All + User.Read.All
            // are fully consented, so distinguish that case from a genuine consent gap.
            var detail = $"{odataEx.Error?.Code} {odataEx.Error?.Message} {odataEx.Message}";
            if (IsPremiumLicenseError(detail))
            {
                _logger.LogWarning("Inactive account data unavailable for tenant {TenantId}: reading signInActivity " +
                    "requires a Microsoft Entra ID P1/P2 license (permissions are consented). Detail: {Detail}",
                    tenantId, detail.Trim());
            }
            else
            {
                _logger.LogWarning("Inactive account data unavailable for tenant {TenantId}: insufficient permissions. " +
                    "Requires AuditLog.Read.All + User.Read.All. Detail: {Detail}", tenantId, detail.Trim());
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get inactive account details for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // signInActivity (and several other sign-in-derived properties) require an Entra ID P1/P2 license.
    // Graph signals this with a 403 whose body mentions premium/license rather than a missing permission.
    private static bool IsPremiumLicenseError(string detail)
    {
        if (string.IsNullOrEmpty(detail)) return false;
        return detail.Contains("premium", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("P1", StringComparison.Ordinal)
            || detail.Contains("P2", StringComparison.Ordinal)
            || detail.Contains("Aad Premium", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("does not have a valid license", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("RequestFromNonPremiumTenant", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("B2C", StringComparison.OrdinalIgnoreCase);
    }

    // Service Health — per-service status overview + active service issues (incidents/advisories)
    public async Task<(List<ServiceHealthSnapshot> Services, List<ServiceHealthIssueSnapshot> Issues)> GetServiceHealthAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var services = new List<ServiceHealthSnapshot>();
        var issues = new List<ServiceHealthIssueSnapshot>();
        var reportDate = DateTime.UtcNow.Date;

        try
        {
            // Per-service health overview
            var overviews = await client.Admin.ServiceAnnouncement.HealthOverviews.GetAsync();
            if (overviews?.Value != null)
            {
                foreach (var o in overviews.Value)
                {
                    if (string.IsNullOrEmpty(o.Service)) continue;
                    services.Add(new ServiceHealthSnapshot
                    {
                        TenantId = tenantId,
                        ReportDate = reportDate,
                        ServiceName = o.Service,
                        Status = o.Status?.ToString() ?? "Unknown",
                        CollectedAt = DateTime.UtcNow
                    });
                }
            }

            // Active service issues (incidents + advisories)
            var issuePage = await client.Admin.ServiceAnnouncement.Issues.GetAsync(config =>
            {
                config.QueryParameters.Top = 100;
            });

            if (issuePage?.Value != null)
            {
                void Accumulate(Microsoft.Graph.Models.ServiceHealthIssue i)
                {
                    if (string.IsNullOrEmpty(i.Id)) return;
                    // Only keep unresolved issues — resolved ones aren't actionable
                    if (i.IsResolved == true) return;

                    issues.Add(new ServiceHealthIssueSnapshot
                    {
                        TenantId = tenantId,
                        ReportDate = reportDate,
                        IssueId = i.Id,
                        Title = i.Title ?? string.Empty,
                        ServiceName = i.Service ?? string.Empty,
                        Classification = i.Classification?.ToString() ?? string.Empty,
                        Status = i.Status?.ToString() ?? string.Empty,
                        Feature = i.Feature ?? string.Empty,
                        StartDateTime = i.StartDateTime?.UtcDateTime,
                        IsResolved = i.IsResolved ?? false,
                        CollectedAt = DateTime.UtcNow
                    });
                }

                var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.ServiceHealthIssue, Microsoft.Graph.Models.ServiceHealthIssueCollectionResponse>
                    .CreatePageIterator(client, issuePage, i => { Accumulate(i); return true; });
                await iterator.IterateAsync();
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Service health data unavailable for tenant {TenantId}: insufficient permissions. " +
                "Requires ServiceHealth.Read.All.", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get service health for tenant {TenantId}", tenantId);
        }

        return (services, issues);
    }

    // Intune device compliance — tenant-level point-in-time counts of managed devices by
    // compliance state and operating system. Requires DeviceManagementManagedDevices.Read.All.
    public async Task<DeviceComplianceSnapshot?> GetDeviceComplianceAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.DeviceManagement.ManagedDevices.GetAsync(c =>
            {
                c.QueryParameters.Select = ["id", "complianceState", "operatingSystem"];
                c.QueryParameters.Top = 999;
            });

            if (page?.Value == null) return null;

            var snapshot = new DeviceComplianceSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            void Accumulate(Microsoft.Graph.Models.ManagedDevice d)
            {
                snapshot.TotalDevices++;

                switch (d.ComplianceState)
                {
                    case Microsoft.Graph.Models.ComplianceState.Compliant:
                        snapshot.CompliantCount++; break;
                    case Microsoft.Graph.Models.ComplianceState.Noncompliant:
                        snapshot.NonCompliantCount++; break;
                    case Microsoft.Graph.Models.ComplianceState.InGracePeriod:
                        snapshot.InGracePeriodCount++; break;
                    case Microsoft.Graph.Models.ComplianceState.Error:
                        snapshot.ErrorCount++; break;
                    default:
                        snapshot.UnknownCount++; break;
                }

                var os = (d.OperatingSystem ?? string.Empty).ToLowerInvariant();
                if (os.Contains("windows")) snapshot.WindowsCount++;
                else if (os.Contains("ios") || os.Contains("ipados")) snapshot.IosCount++;
                else if (os.Contains("android")) snapshot.AndroidCount++;
                else if (os.Contains("mac")) snapshot.MacOsCount++;
                else snapshot.OtherOsCount++;
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.ManagedDevice, Microsoft.Graph.Models.ManagedDeviceCollectionResponse>
                .CreatePageIterator(client, page, d => { Accumulate(d); return true; });
            await iterator.IterateAsync();

            return snapshot;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Device compliance unavailable for tenant {TenantId}: insufficient permissions. " +
                "Requires DeviceManagementManagedDevices.Read.All.", tenantId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get device compliance for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // Conditional Access coverage — counts policies by state and detects whether key
    // protections (legacy-auth block, MFA grant) exist in any enabled policy.
    // Requires Policy.Read.All.
    public async Task<ConditionalAccessSnapshot?> GetConditionalAccessAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.Identity.ConditionalAccess.Policies.GetAsync();
            if (page?.Value == null) return null;

            var snapshot = new ConditionalAccessSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            foreach (var p in page.Value)
            {
                snapshot.TotalPolicies++;
                var enabled = p.State == Microsoft.Graph.Models.ConditionalAccessPolicyState.Enabled;
                switch (p.State)
                {
                    case Microsoft.Graph.Models.ConditionalAccessPolicyState.Enabled:
                        snapshot.EnabledPolicies++; break;
                    case Microsoft.Graph.Models.ConditionalAccessPolicyState.EnabledForReportingButNotEnforced:
                        snapshot.ReportOnlyPolicies++; break;
                    default:
                        snapshot.DisabledPolicies++; break;
                }

                if (!enabled) continue;

                // MFA grant control present?
                var controls = p.GrantControls?.BuiltInControls;
                if (controls != null && controls.Contains(Microsoft.Graph.Models.ConditionalAccessGrantControl.Mfa))
                    snapshot.RequiresMfa = true;

                // Legacy-auth block: a block policy targeting the legacy client app types
                var clientApps = p.Conditions?.ClientAppTypes;
                var targetsLegacy = clientApps != null && (
                    clientApps.Contains(Microsoft.Graph.Models.ConditionalAccessClientApp.ExchangeActiveSync) ||
                    clientApps.Contains(Microsoft.Graph.Models.ConditionalAccessClientApp.Other));
                var blocks = controls != null && controls.Contains(Microsoft.Graph.Models.ConditionalAccessGrantControl.Block);
                if (targetsLegacy && blocks)
                    snapshot.BlocksLegacyAuth = true;
            }

            return snapshot;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Conditional Access data unavailable for tenant {TenantId}: insufficient permissions. " +
                "Requires Policy.Read.All.", tenantId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Conditional Access policies for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // Guest / external users — tenant-level governance counts. Uses User.Read.All (already
    // granted) and avoids signInActivity so it works on all license tiers.
    public async Task<GuestUserSnapshot?> GetGuestUsersAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.Users.GetAsync(c =>
            {
                c.QueryParameters.Filter = "userType eq 'Guest'";
                c.QueryParameters.Select = ["id", "externalUserState", "createdDateTime"];
                c.QueryParameters.Top = 999;
            });

            if (page?.Value == null) return null;

            var snapshot = new GuestUserSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            var recentCutoff = DateTimeOffset.UtcNow.AddDays(-30);

            void Accumulate(Microsoft.Graph.Models.User u)
            {
                snapshot.TotalGuests++;

                if (string.Equals(u.ExternalUserState, "PendingAcceptance", StringComparison.OrdinalIgnoreCase))
                    snapshot.PendingAcceptanceGuests++;
                else if (string.Equals(u.ExternalUserState, "Accepted", StringComparison.OrdinalIgnoreCase))
                    snapshot.AcceptedGuests++;

                if (u.CreatedDateTime != null && u.CreatedDateTime >= recentCutoff)
                    snapshot.RecentlyAddedGuests++;
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.User, Microsoft.Graph.Models.UserCollectionResponse>
                .CreatePageIterator(client, page, u => { Accumulate(u); return true; });
            await iterator.IterateAsync();

            return snapshot;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Guest user data unavailable for tenant {TenantId}: insufficient permissions. " +
                "Requires User.Read.All.", tenantId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get guest users for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // Risky users (Identity Protection) — counts at-risk users by risk level.
    // Requires IdentityRiskyUser.Read.All AND Entra ID P2 on the target tenant.
    public async Task<RiskyUserSnapshot?> GetRiskyUsersAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.IdentityProtection.RiskyUsers.GetAsync(c =>
            {
                c.QueryParameters.Select = ["id", "riskLevel", "riskState"];
                c.QueryParameters.Top = 999;
            });

            if (page?.Value == null) return null;

            var snapshot = new RiskyUserSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            void Accumulate(Microsoft.Graph.Models.RiskyUser r)
            {
                // Only count users still considered a risk
                var atRisk = r.RiskState == Microsoft.Graph.Models.RiskState.AtRisk
                    || r.RiskState == Microsoft.Graph.Models.RiskState.ConfirmedCompromised;
                if (!atRisk) return;

                snapshot.TotalAtRisk++;
                switch (r.RiskLevel)
                {
                    case Microsoft.Graph.Models.RiskLevel.High:
                        snapshot.HighRisk++; break;
                    case Microsoft.Graph.Models.RiskLevel.Medium:
                        snapshot.MediumRisk++; break;
                    case Microsoft.Graph.Models.RiskLevel.Low:
                        snapshot.LowRisk++; break;
                }
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.RiskyUser, Microsoft.Graph.Models.RiskyUserCollectionResponse>
                .CreatePageIterator(client, page, r => { Accumulate(r); return true; });
            await iterator.IterateAsync();

            return snapshot;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Risky user data unavailable for tenant {TenantId}: insufficient permissions or license. " +
                "Requires IdentityRiskyUser.Read.All + Entra ID P2.", tenantId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get risky users for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // ---- Tier 3: Usage & Governance ----

    private const int TopN = 20;

    // Mailbox usage — tenant aggregate (detail + quota-status counts) plus top-N largest mailboxes.
    public async Task<(MailboxUsageSnapshot? Aggregate, List<TopMailboxSnapshot> Top)> GetMailboxUsageAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var reportDate = DateTime.UtcNow.Date;
        MailboxUsageSnapshot? aggregate = null;
        var top = new List<TopMailboxSnapshot>();

        // Per-mailbox detail → totals + top-N
        try
        {
            var report = await client.Reports.GetMailboxUsageDetailWithPeriod("D30").GetAsync();
            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                var rows = ParseCsv(csv);
                if (rows.Count > 1)
                {
                    var h = rows[0];
                    int iName = Array.IndexOf(h, "Display Name");
                    int iStorage = Array.IndexOf(h, "Storage Used (Byte)");
                    int iItems = Array.IndexOf(h, "Item Count");
                    int iLast = Array.IndexOf(h, "Last Activity Date");

                    aggregate = new MailboxUsageSnapshot { TenantId = tenantId, ReportDate = reportDate, CollectedAt = DateTime.UtcNow };
                    var cutoff = DateTime.UtcNow.AddDays(-30);
                    var mailboxes = new List<TopMailboxSnapshot>();

                    for (int r = 1; r < rows.Count; r++)
                    {
                        var v = rows[r];
                        aggregate.TotalMailboxes++;
                        long.TryParse(GetValue(v, iStorage), out var storage);
                        long.TryParse(GetValue(v, iItems), out var items);
                        aggregate.TotalStorageUsedBytes += storage;

                        var hasActivity = DateTime.TryParse(GetValue(v, iLast), out var last);
                        if (hasActivity && last >= cutoff) aggregate.ActiveMailboxes++;
                        else aggregate.InactiveMailboxes++;

                        mailboxes.Add(new TopMailboxSnapshot
                        {
                            TenantId = tenantId,
                            ReportDate = reportDate,
                            DisplayName = GetValue(v, iName) ?? "(unknown)",
                            StorageUsedBytes = storage,
                            ItemCount = items,
                            LastActivityDate = hasActivity ? last : null,
                            CollectedAt = DateTime.UtcNow
                        });
                    }

                    top = mailboxes.OrderByDescending(m => m.StorageUsedBytes).Take(TopN).ToList();
                    for (int rank = 0; rank < top.Count; rank++) top[rank].Rank = rank + 1;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get mailbox usage detail for tenant {TenantId}", tenantId);
        }

        // Quota-status counts → latest report date
        try
        {
            var report = await client.Reports.GetMailboxUsageQuotaStatusMailboxCountsWithPeriod("D30").GetAsync();
            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                var rows = ParseCsv(csv);
                if (rows.Count > 1)
                {
                    var h = rows[0];
                    int iDate = Array.IndexOf(h, "Report Date");
                    int iUnder = Array.IndexOf(h, "Under Limit");
                    int iWarn = Array.IndexOf(h, "Warning Issued");
                    int iSend = Array.IndexOf(h, "Send Prohibited");
                    int iSendRecv = Array.IndexOf(h, "Send/Receive Prohibited");

                    // These time-series count reports always emit the current refresh-date row with
                    // EMPTY count columns; the populated figures sit on an earlier day. Pick the most
                    // recent row that actually carries count data rather than the strictly latest date.
                    var latest = LatestRowWithData(rows, iDate, iUnder, iWarn, iSend, iSendRecv);
                    if (latest != null)
                    {
                        aggregate ??= new MailboxUsageSnapshot { TenantId = tenantId, ReportDate = reportDate, CollectedAt = DateTime.UtcNow };
                        int.TryParse(GetValue(latest, iUnder), out var under); aggregate.UnderLimitCount = under;
                        int.TryParse(GetValue(latest, iWarn), out var warn); aggregate.WarningCount = warn;
                        int.TryParse(GetValue(latest, iSend), out var send); aggregate.SendProhibitedCount = send;
                        int.TryParse(GetValue(latest, iSendRecv), out var sr); aggregate.SendReceiveProhibitedCount = sr;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get mailbox quota-status counts for tenant {TenantId}", tenantId);
        }

        return (aggregate, top);
    }

    // Teams device usage — latest per-device-type user counts.
    public async Task<TeamsDeviceUsageSnapshot?> GetTeamsDeviceUsageAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        try
        {
            var report = await client.Reports.GetTeamsDeviceUsageUserCountsWithPeriod("D30").GetAsync();
            if (report == null) return null;

            using var reader = new StreamReader(report);
            var csv = await reader.ReadToEndAsync();
            var rows = ParseCsv(csv);
            if (rows.Count < 2) return null;

            var h = rows[0];
            int iDate = Array.IndexOf(h, "Report Date");
            var latest = LatestByDate(rows, iDate);
            if (latest == null) return null;

            int Col(string name) { int idx = Array.IndexOf(h, name); int.TryParse(GetValue(latest, idx), out var c); return c; }

            return new TeamsDeviceUsageSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                WindowsCount = Col("Windows"),
                MacCount = Col("Mac"),
                WebCount = Col("Web"),
                IosCount = Col("iOS"),
                AndroidPhoneCount = Col("Android Phone"),
                WindowsPhoneCount = Col("Windows Phone"),
                ChromeOsCount = Col("Chrome OS"),
                LinuxCount = Col("Linux"),
                CollectedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Teams device usage for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // SharePoint sites + OneDrive accounts — per-workload aggregate plus top-N by storage.
    public async Task<(List<SiteUsageSnapshot> Aggregates, List<SiteUsageDetailSnapshot> Details)> GetSiteUsageAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var aggregates = new List<SiteUsageSnapshot>();
        var details = new List<SiteUsageDetailSnapshot>();
        var reportDate = DateTime.UtcNow.Date;

        async Task CollectAsync(string workload, Func<Task<Stream?>> fetch, string nameColumn)
        {
            try
            {
                var report = await fetch();
                if (report == null) return;
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                var rows = ParseCsv(csv);
                if (rows.Count < 2) return;

                var h = rows[0];
                int iName = Array.IndexOf(h, nameColumn);
                int iStorage = Array.IndexOf(h, "Storage Used (Byte)");
                int iFiles = Array.IndexOf(h, "File Count");
                int iActive = Array.IndexOf(h, "Active File Count");
                int iLast = Array.IndexOf(h, "Last Activity Date");

                var agg = new SiteUsageSnapshot { TenantId = tenantId, ReportDate = reportDate, Workload = workload, CollectedAt = DateTime.UtcNow };
                var items = new List<SiteUsageDetailSnapshot>();

                for (int r = 1; r < rows.Count; r++)
                {
                    var v = rows[r];
                    agg.TotalSites++;
                    long.TryParse(GetValue(v, iStorage), out var storage);
                    long.TryParse(GetValue(v, iFiles), out var files);
                    long.TryParse(GetValue(v, iActive), out var active);
                    agg.TotalStorageUsedBytes += storage;
                    agg.TotalFileCount += files;
                    agg.ActiveFileCount += active;
                    var hasActivity = DateTime.TryParse(GetValue(v, iLast), out var last);
                    if (active > 0) agg.ActiveSites++;

                    items.Add(new SiteUsageDetailSnapshot
                    {
                        TenantId = tenantId,
                        ReportDate = reportDate,
                        Workload = workload,
                        Name = GetValue(v, iName) ?? "(unknown)",
                        StorageUsedBytes = storage,
                        FileCount = files,
                        ActiveFileCount = active,
                        LastActivityDate = hasActivity ? last : null,
                        CollectedAt = DateTime.UtcNow
                    });
                }

                aggregates.Add(agg);
                var topItems = items.OrderByDescending(i => i.StorageUsedBytes).Take(TopN).ToList();
                for (int rank = 0; rank < topItems.Count; rank++) topItems[rank].Rank = rank + 1;
                details.AddRange(topItems);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get {Workload} site usage detail for tenant {TenantId}", workload, tenantId);
            }
        }

        await CollectAsync(M365Services.SharePoint,
            () => client.Reports.GetSharePointSiteUsageDetailWithPeriod("D30").GetAsync(), "Site URL");
        await CollectAsync(M365Services.OneDrive,
            () => client.Reports.GetOneDriveUsageAccountDetailWithPeriod("D30").GetAsync(), "Owner Display Name");

        return (aggregates, details);
    }

    // Viva Engage (Yammer) activity — latest posted/read/liked user counts.
    public async Task<YammerActivitySnapshot?> GetYammerActivityAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        try
        {
            var report = await client.Reports.GetYammerActivityUserCountsWithPeriod("D30").GetAsync();
            if (report == null) return null;

            using var reader = new StreamReader(report);
            var csv = await reader.ReadToEndAsync();
            var rows = ParseCsv(csv);
            if (rows.Count < 2) return null;

            var h = rows[0];
            int iDate = Array.IndexOf(h, "Report Date");
            var latest = LatestByDate(rows, iDate);
            if (latest == null) return null;

            int Col(string name) { int idx = Array.IndexOf(h, name); int.TryParse(GetValue(latest, idx), out var c); return c; }

            return new YammerActivitySnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                PostedCount = Col("Posted"),
                ReadCount = Col("Read"),
                LikedCount = Col("Liked"),
                CollectedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Yammer activity for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // Groups & Teams sprawl — counts of group types and ownerless M365 groups. Requires Group.Read.All.
    public async Task<GroupSnapshot?> GetGroupSprawlAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        try
        {
            var page = await client.Groups.GetAsync(c =>
            {
                c.QueryParameters.Select = ["id", "groupTypes", "resourceProvisioningOptions", "securityEnabled", "mailEnabled"];
                c.QueryParameters.Expand = ["owners($select=id)"];
                c.QueryParameters.Top = 999;
            });

            if (page?.Value == null) return null;

            var snapshot = new GroupSnapshot { TenantId = tenantId, ReportDate = DateTime.UtcNow.Date, CollectedAt = DateTime.UtcNow };

            void Accumulate(Microsoft.Graph.Models.Group g)
            {
                snapshot.TotalGroups++;
                var isUnified = g.GroupTypes != null && g.GroupTypes.Contains("Unified");
                var isTeam = g.AdditionalData != null
                    && g.AdditionalData.TryGetValue("resourceProvisioningOptions", out var rpo)
                    && rpo?.ToString()?.Contains("Team", StringComparison.OrdinalIgnoreCase) == true;

                if (isUnified)
                {
                    snapshot.M365Groups++;
                    if (g.Owners == null || g.Owners.Count == 0) snapshot.OwnerlessGroups++;
                }
                else if (g.SecurityEnabled == true)
                {
                    snapshot.SecurityGroups++;
                }
                else if (g.MailEnabled == true)
                {
                    // Mail-enabled, non-security, non-unified = classic distribution list
                    snapshot.DistributionGroups++;
                }

                if (isTeam) snapshot.TeamsConnectedGroups++;
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.Group, Microsoft.Graph.Models.GroupCollectionResponse>
                .CreatePageIterator(client, page, g => { Accumulate(g); return true; });
            await iterator.IterateAsync();

            return snapshot;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Group sprawl data unavailable for tenant {TenantId}: insufficient permissions. Requires Group.Read.All.", tenantId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get group sprawl for tenant {TenantId}", tenantId);
            return null;
        }
    }

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
        // IdentityRiskyUser.Read.All is intentionally NOT probed here: it is Entra ID P2-gated,
        // so a 403 on a non-P2 tenant is a licensing limit, not a consent gap, and would produce
        // a false "re-consent" flag. The risky-user collection logs that case on its own.

        return missing;
    }
    private async Task ProbePermissionAsync(List<string> missing, string permission, Func<Task> probe)
    {
        try
        {
            await probe();
        }
        catch (Exception ex)
        {
            if (IsPermissionError(ex))
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

    private static bool IsPermissionError(Exception ex)
    {
        if (ex is Microsoft.Graph.Models.ODataErrors.ODataError odata)
        {
            var detail = $"{odata.Error?.Code} {odata.Error?.Message} {odata.Message}";

            // A premium-license 403 (e.g. signInActivity needs Entra ID P1/P2) is NOT a consent gap —
            // the permission is granted, the tenant just isn't licensed for the data. Don't flag it.
            if (IsPremiumLicenseError(detail))
                return false;

            if (odata.ResponseStatusCode == 403) return true;
            var code = odata.Error?.Code ?? string.Empty;
            if (code.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var msg = ex.Message ?? string.Empty;
        return msg.Contains("Invalid permission", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("does not have required", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("S2SUnauthorized", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase);
    }
}
