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

        var headers = lines[0].Split(',');
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
                activities.AddRange(ParseTeamsActivity(csv, tenantId));
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

        var headers = lines[0].Split(',');
        var dateIndex = Array.IndexOf(headers, "Report Date");
        var meetingsIndex = Array.IndexOf(headers, "Meetings Organized");
        var chatIndex = Array.IndexOf(headers, "Team Chat Messages");
        var channelIndex = Array.IndexOf(headers, "Post Messages");
        var callsIndex = Array.IndexOf(headers, "Calls");

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

        var headers = lines[0].Split(',');
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

        var headers = lines[0].Split(',');
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

        var headers = lines[0].Split(',');
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
                if (!int.TryParse(values[colIndex].Trim(), out var count) || count == 0) continue;

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
}
