using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using MWDashboard.Models;
using BetaGraphClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace MWDashboard.Services;

public interface IGraphReportService
{
    Task<List<MauSnapshot>> GetActiveUserCountsAsync(string tenantId, int periodDays = 180);
    Task<List<LicenseSnapshot>> GetSubscribedSkusAsync(string tenantId);
    Task<List<MessageCenterPost>> GetMessageCenterPostsAsync(string tenantId);
    Task<List<SecuritySignInSummary>> GetSignInSummaryAsync(string tenantId, int days = 30);
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
            // Use Beta API for access to AuthenticationDetails (true MFA data)
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
                // Group by date and app for daily summaries
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
                        // Beta API: AuthenticationDetails contains each auth step; MFA = any non-password method succeeded
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
