using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using MWDashboard.Shared.Models;
using BetaGraphClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace MWDashboard.Shared.Services;

public partial class GraphReportService
{
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
}
