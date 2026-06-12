using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using MWDashboard.Shared.Models;
using BetaGraphClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace MWDashboard.Shared.Services;

public partial class GraphReportService
{
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
                    int iRefresh = Array.IndexOf(h, "Report Refresh Date");
                    int iUnder = Array.IndexOf(h, "Under Limit");
                    int iWarn = Array.IndexOf(h, "Warning Issued");
                    int iSend = Array.IndexOf(h, "Send Prohibited");
                    int iSendRecv = Array.IndexOf(h, "Send/Receive Prohibited");

                    // These time-series count reports always emit the current refresh-date row with
                    // EMPTY count columns; the populated figures sit on an earlier day. Pick the most
                    // recent row that actually carries count data rather than the strictly latest date.
                    // This aggregate report also commonly leaves "Report Date" blank (only
                    // "Report Refresh Date" is populated), so fall back to it for row selection.
                    var latest = LatestRowWithData(rows, iDate, iUnder, iWarn, iSend, iSendRecv)
                              ?? LatestRowWithData(rows, iRefresh, iUnder, iWarn, iSend, iSendRecv);
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

    // App registration / enterprise-app credential expiry — one row per secret or certificate.
    // Requires Application.Read.All.
}
