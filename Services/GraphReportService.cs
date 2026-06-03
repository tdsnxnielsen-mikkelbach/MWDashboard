using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using MWDashboard.Models;

namespace MWDashboard.Services;

public interface IGraphReportService
{
    Task<List<MauSnapshot>> GetActiveUserCountsAsync(string tenantId, int periodDays = 180);
    Task<List<LicenseSnapshot>> GetSubscribedSkusAsync(string tenantId);
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
            // Get Office 365 active user counts
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
                    licenses.Add(new LicenseSnapshot
                    {
                        TenantId = tenantId,
                        SkuId = sku.SkuId?.ToString() ?? string.Empty,
                        SkuPartNumber = sku.SkuPartNumber ?? string.Empty,
                        TotalLicenses = sku.PrepaidUnits?.Enabled ?? 0,
                        ConsumedLicenses = sku.ConsumedUnits ?? 0,
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

    private static List<MauSnapshot> ParseActiveUserCounts(string csv, string tenantId)
    {
        var snapshots = new List<MauSnapshot>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2) return snapshots;

        var headers = lines[0].Split(',');
        var dateIndex = Array.IndexOf(headers, "Report Date");

        // Map column indices to service names
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
}
