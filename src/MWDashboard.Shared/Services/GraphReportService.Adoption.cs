using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using MWDashboard.Shared.Models;
using BetaGraphClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace MWDashboard.Shared.Services;

public partial class GraphReportService
{
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

}
