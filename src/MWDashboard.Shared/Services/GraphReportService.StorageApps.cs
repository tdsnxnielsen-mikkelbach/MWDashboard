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

        // App-level user counts (Outlook, Word, Excel, PowerPoint, OneNote, Teams).
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

        // Platform-level user counts (Windows, Mac, Mobile, Web) — a SEPARATE Graph report;
        // getM365AppUserCounts does not contain platform columns.
        try
        {
            var report = await client.Reports
                .GetM365AppPlatformUserCountsWithPeriod("D30")
                .GetAsync();

            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                snapshots.AddRange(ParseM365AppPlatformUsage(csv, tenantId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get M365 App platform usage for tenant {TenantId}", tenantId);
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
        }

        return snapshots;
    }

    // Parses the getM365AppPlatformUserCounts report (Windows / Mac / Mobile / Web).
    private static List<M365AppUsageSnapshot> ParseM365AppPlatformUsage(string csv, string tenantId)
    {
        var snapshots = new List<M365AppUsageSnapshot>();
        var rows = ParseCsv(csv);
        if (rows.Count < 2) return snapshots;

        var h = rows[0];
        int iDate = Array.IndexOf(h, "Report Date");
        int iRefresh = Array.IndexOf(h, "Report Refresh Date");

        var platformColumns = new Dictionary<string, int>
        {
            ["Windows"] = Array.IndexOf(h, "Windows"),
            ["Mac"] = Array.IndexOf(h, "Mac"),
            ["Mobile"] = Array.IndexOf(h, "Mobile"),
            ["Web"] = Array.IndexOf(h, "Web")
        };

        var valueIndexes = platformColumns.Values.Where(i => i >= 0).ToArray();
        if (valueIndexes.Length == 0) return snapshots;

        // Pick the most recent row that actually carries counts; these reports emit the current
        // refresh-date row blank and may leave "Report Date" empty, so fall back to refresh date.
        var latest = LatestRowWithData(rows, iDate, valueIndexes)
                  ?? LatestRowWithData(rows, iRefresh, valueIndexes);
        if (latest == null) return snapshots;

        if (!DateTime.TryParse(GetValue(latest, iDate), out var date)
            && !DateTime.TryParse(GetValue(latest, iRefresh), out date))
        {
            date = DateTime.UtcNow.Date;
        }

        foreach (var (platform, colIndex) in platformColumns)
        {
            if (colIndex < 0 || colIndex >= latest.Length) continue;
            if (!int.TryParse(latest[colIndex].Trim(), out var count) || count == 0) continue;

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

        return snapshots;
    }

    // ---- M365 Apps per-user detail (getM365AppUserDetail) — anonymized app x platform matrix ----

    public async Task<List<M365AppUserDetailSnapshot>> GetM365AppUserDetailAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var snapshots = new List<M365AppUserDetailSnapshot>();

        try
        {
            var report = await client.Reports
                .GetM365AppUserDetailWithPeriod("D30")
                .GetAsync();

            if (report != null)
            {
                using var reader = new StreamReader(report);
                var csv = await reader.ReadToEndAsync();
                snapshots.AddRange(ParseM365AppUserDetail(csv, tenantId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get M365 App user detail for tenant {TenantId}", tenantId);
        }

        return snapshots;
    }

    private List<M365AppUserDetailSnapshot> ParseM365AppUserDetail(string csv, string tenantId)
    {
        var snapshots = new List<M365AppUserDetailSnapshot>();
        var rows = ParseCsv(csv);
        if (rows.Count < 2) return snapshots;

        var h = rows[0];
        int iRefresh = Array.IndexOf(h, "Report Refresh Date");
        int iUpn = Array.IndexOf(h, "User Principal Name");
        int iActivation = Array.IndexOf(h, "Last Activation Date");
        int iActivity = Array.IndexOf(h, "Last Activity Date");

        int Idx(string col) => Array.IndexOf(h, col);

        for (int r = 1; r < rows.Count; r++)
        {
            var v = rows[r];
            var upn = GetValue(v, iUpn);
            if (string.IsNullOrWhiteSpace(upn)) continue;

            bool Used(string col) { int i = Idx(col); return i >= 0 && i < v.Length && IsYes(v[i]); }

            snapshots.Add(new M365AppUserDetailSnapshot
            {
                TenantId = tenantId,
                ReportDate = ParseDate(GetValue(v, iRefresh)) ?? DateTime.UtcNow.Date,
                UserKey = Pseudonymize(upn, tenantId),
                LastActivityDate = ParseDate(GetValue(v, iActivity)),
                LastActivationDate = ParseDate(GetValue(v, iActivation)),

                OutlookWindows = Used("Outlook (Windows)"),
                WordWindows = Used("Word (Windows)"),
                ExcelWindows = Used("Excel (Windows)"),
                PowerPointWindows = Used("PowerPoint (Windows)"),
                OneNoteWindows = Used("OneNote (Windows)"),
                TeamsWindows = Used("Teams (Windows)"),

                OutlookMac = Used("Outlook (Mac)"),
                WordMac = Used("Word (Mac)"),
                ExcelMac = Used("Excel (Mac)"),
                PowerPointMac = Used("PowerPoint (Mac)"),
                OneNoteMac = Used("OneNote (Mac)"),
                TeamsMac = Used("Teams (Mac)"),

                OutlookMobile = Used("Outlook (Mobile)"),
                WordMobile = Used("Word (Mobile)"),
                ExcelMobile = Used("Excel (Mobile)"),
                PowerPointMobile = Used("PowerPoint (Mobile)"),
                OneNoteMobile = Used("OneNote (Mobile)"),
                TeamsMobile = Used("Teams (Mobile)"),

                OutlookWeb = Used("Outlook (Web)"),
                WordWeb = Used("Word (Web)"),
                ExcelWeb = Used("Excel (Web)"),
                PowerPointWeb = Used("PowerPoint (Web)"),
                OneNoteWeb = Used("OneNote (Web)"),
                TeamsWeb = Used("Teams (Web)"),

                CollectedAt = DateTime.UtcNow
            });
        }

        return snapshots;
    }

    // ---- Office activations (getOffice365ActivationCounts + getOffice365ActivationsUserDetail) ----

    public async Task<(List<Office365ActivationSnapshot> Counts, List<Office365ActivationUserSnapshot> Users)> GetOffice365ActivationsAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var counts = new List<Office365ActivationSnapshot>();
        var users = new List<Office365ActivationUserSnapshot>();

        // Aggregate activation counts per product type & device platform (no PII).
        try
        {
            var report = await client.Reports.GetOffice365ActivationCounts.GetAsync();
            if (report != null)
            {
                using var reader = new StreamReader(report);
                counts.AddRange(ParseOffice365ActivationCounts(await reader.ReadToEndAsync(), tenantId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Office 365 activation counts for tenant {TenantId}", tenantId);
        }

        // Per-user activation detail (UPN pseudonymized; Display Name dropped).
        try
        {
            var report = await client.Reports.GetOffice365ActivationsUserDetail.GetAsync();
            if (report != null)
            {
                using var reader = new StreamReader(report);
                users.AddRange(ParseOffice365ActivationUsers(await reader.ReadToEndAsync(), tenantId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Office 365 activation user detail for tenant {TenantId}", tenantId);
        }

        return (counts, users);
    }

    private static List<Office365ActivationSnapshot> ParseOffice365ActivationCounts(string csv, string tenantId)
    {
        var list = new List<Office365ActivationSnapshot>();
        var rows = ParseCsv(csv);
        if (rows.Count < 2) return list;

        var h = rows[0];
        int iRefresh = Array.IndexOf(h, "Report Refresh Date");
        int iProduct = Array.IndexOf(h, "Product Type");
        int iWin = Array.IndexOf(h, "Windows");
        int iMac = Array.IndexOf(h, "Mac");
        int iAndroid = Array.IndexOf(h, "Android");
        int iIos = Array.IndexOf(h, "iOS");
        int iWinMobile = Array.IndexOf(h, "Windows 10 Mobile");

        for (int r = 1; r < rows.Count; r++)
        {
            var v = rows[r];
            var product = GetValue(v, iProduct);
            if (string.IsNullOrWhiteSpace(product)) continue;

            int.TryParse(GetValue(v, iWin), out var win);
            int.TryParse(GetValue(v, iMac), out var mac);
            int.TryParse(GetValue(v, iAndroid), out var android);
            int.TryParse(GetValue(v, iIos), out var ios);
            int.TryParse(GetValue(v, iWinMobile), out var winMobile);

            list.Add(new Office365ActivationSnapshot
            {
                TenantId = tenantId,
                ReportDate = ParseDate(GetValue(v, iRefresh)) ?? DateTime.UtcNow.Date,
                ProductType = product!,
                WindowsCount = win,
                MacCount = mac,
                AndroidCount = android,
                IosCount = ios,
                WindowsMobileCount = winMobile,
                CollectedAt = DateTime.UtcNow
            });
        }

        return list;
    }

    private List<Office365ActivationUserSnapshot> ParseOffice365ActivationUsers(string csv, string tenantId)
    {
        var list = new List<Office365ActivationUserSnapshot>();
        var rows = ParseCsv(csv);
        if (rows.Count < 2) return list;

        var h = rows[0];
        int iRefresh = Array.IndexOf(h, "Report Refresh Date");
        int iUpn = Array.IndexOf(h, "User Principal Name");
        int iProduct = Array.IndexOf(h, "Product Type");
        int iLast = Array.IndexOf(h, "Last Activated Date");
        int iWin = Array.IndexOf(h, "Windows");
        int iMac = Array.IndexOf(h, "Mac");
        int iWinMobile = Array.IndexOf(h, "Windows 10 Mobile");
        int iIos = Array.IndexOf(h, "iOS");
        int iAndroid = Array.IndexOf(h, "Android");
        int iShared = Array.IndexOf(h, "Activated On Shared Computer");

        bool Used(string[] v, int i) => i >= 0 && i < v.Length && IsYes(v[i]);

        for (int r = 1; r < rows.Count; r++)
        {
            var v = rows[r];
            var upn = GetValue(v, iUpn);
            var product = GetValue(v, iProduct);
            if (string.IsNullOrWhiteSpace(upn) || string.IsNullOrWhiteSpace(product)) continue;

            list.Add(new Office365ActivationUserSnapshot
            {
                TenantId = tenantId,
                ReportDate = ParseDate(GetValue(v, iRefresh)) ?? DateTime.UtcNow.Date,
                UserKey = Pseudonymize(upn, tenantId),
                ProductType = product!,
                LastActivatedDate = ParseDate(GetValue(v, iLast)),
                Windows = Used(v, iWin),
                Mac = Used(v, iMac),
                WindowsMobile = Used(v, iWinMobile),
                Ios = Used(v, iIos),
                Android = Used(v, iAndroid),
                SharedComputer = Used(v, iShared),
                CollectedAt = DateTime.UtcNow
            });
        }

        return list;
    }

    private static bool IsYes(string? s)
    {
        s = s?.Trim();
        return string.Equals(s, "Yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
            || s == "1";
    }

    private static DateTime? ParseDate(string? s)
        => DateTime.TryParse(s, out var d) ? d : null;

    // Microsoft Secure Score — daily tenant score trend + per-control remediation actions
}

