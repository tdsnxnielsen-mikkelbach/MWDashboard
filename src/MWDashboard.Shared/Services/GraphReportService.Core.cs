using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using MWDashboard.Shared.Models;
using BetaGraphClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace MWDashboard.Shared.Services;

public partial class GraphReportService
{
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

    // Commercial subscriptions (license renewal / expiry dates) — GET /directory/subscriptions.
    public async Task<List<SubscriptionSnapshot>> GetDirectorySubscriptionsAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var subscriptions = new List<SubscriptionSnapshot>();
        var reportDate = DateTime.UtcNow.Date;

        try
        {
            var subs = await client.Directory.Subscriptions.GetAsync();
            if (subs?.Value != null)
            {
                foreach (var sub in subs.Value)
                {
                    subscriptions.Add(new SubscriptionSnapshot
                    {
                        TenantId = tenantId,
                        ReportDate = reportDate,
                        SkuId = sub.SkuId?.ToString() ?? string.Empty,
                        SkuPartNumber = sub.SkuPartNumber ?? string.Empty,
                        Status = sub.Status ?? string.Empty,
                        IsTrial = sub.IsTrial ?? false,
                        TotalLicenses = sub.TotalLicenses ?? 0,
                        NextLifecycleDateTime = sub.NextLifecycleDateTime?.UtcDateTime,
                        CollectedAt = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
        {
            _logger.LogWarning("Directory subscriptions unavailable for tenant {TenantId}: {Error}. " +
                "Ensure Directory.Read.All permission is granted and admin consent completed.",
                tenantId, odataEx.Error?.Message ?? odataEx.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get directory subscriptions for tenant {TenantId}", tenantId);
        }

        return subscriptions;
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
            var betaClient = CreateBetaClientForTenant(tenantId);

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

    // Legacy-auth & risky sign-in detail — Microsoft Graph (beta) /auditLogs/signIns.
    // Aggregates sign-ins newer than the cursor by (client app, country, day): success/failure
    // counts, risky-sign-in counts (Identity Protection riskLevelAggregated), and a legacy-auth flag
    // (anything other than "Browser"/"Mobile Apps and Desktop clients" bypasses modern auth/MFA).
    // No per-user identities are stored — only aggregate counts. Requires AuditLog.Read.All and is
    // gated behind Entra ID P1/P2 (the caller skips free-tier tenants). The newest sign-in timestamp
    // seen is returned so the caller can advance the per-tenant cursor.
    public async Task<(List<SignInDetailSnapshot> Snapshots, DateTime? MaxCreatedDateTime)> GetSignInDetailAsync(string tenantId, DateTime? sinceUtc)
    {
        var since = sinceUtc ?? DateTime.UtcNow.AddDays(-30);
        // key: (clientApp, country, day) -> (success, failure, risky, isLegacy)
        var buckets = new Dictionary<(string ClientApp, string Country, DateTime Day), (int Success, int Failure, int Risky, bool Legacy)>();
        DateTime? maxCreated = null;

        try
        {
            var betaClient = CreateBetaClientForTenant(tenantId);
            var response = await betaClient.AuditLogs.SignIns.GetAsync(c =>
            {
                c.QueryParameters.Filter = $"createdDateTime ge {since:yyyy-MM-ddTHH:mm:ssZ}";
                c.QueryParameters.Top = 999;
                c.QueryParameters.Select = ["createdDateTime", "clientAppUsed", "status", "location", "riskLevelAggregated"];
                c.QueryParameters.Orderby = ["createdDateTime"];
            });

            while (response?.Value != null)
            {
                foreach (var s in response.Value)
                {
                    var when = s.CreatedDateTime?.UtcDateTime;
                    if (when == null) continue;
                    if (maxCreated == null || when > maxCreated) maxCreated = when;

                    var clientApp = string.IsNullOrEmpty(s.ClientAppUsed) ? "Unknown" : s.ClientAppUsed;
                    var legacy = IsLegacyAuthClient(clientApp);
                    var country = string.IsNullOrEmpty(s.Location?.CountryOrRegion) ? "Unknown" : s.Location.CountryOrRegion;
                    var key = (clientApp, country, when.Value.Date);

                    (int Success, int Failure, int Risky, bool Legacy) agg = buckets.TryGetValue(key, out var a) ? a : (0, 0, 0, legacy);
                    var failed = s.Status?.ErrorCode is not null and not 0;
                    if (failed) agg.Failure++; else agg.Success++;
                    if (IsRiskySignIn(s.RiskLevelAggregated)) agg.Risky++;
                    agg.Legacy = legacy;
                    buckets[key] = agg;
                }

                if (string.IsNullOrEmpty(response.OdataNextLink)) break;
                response = await betaClient.AuditLogs.SignIns.WithUrl(response.OdataNextLink).GetAsync();
            }
        }
        catch (Microsoft.Graph.Beta.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            // Sign-in logs require an Entra ID P1/P2 license; a free-tier tenant returns 403 even when
            // AuditLog.Read.All is fully consented, so distinguish that from a genuine consent gap.
            var detail = $"{odataEx.Error?.Code} {odataEx.Error?.Message} {odataEx.Message}";
            if (IsPremiumLicenseError(detail))
                _logger.LogWarning("Sign-in detail unavailable for tenant {TenantId}: reading sign-in logs requires a Microsoft Entra ID P1/P2 license (permissions are consented). Detail: {Detail}", tenantId, detail.Trim());
            else
                _logger.LogWarning("Sign-in detail unavailable for tenant {TenantId}: insufficient permissions. Requires AuditLog.Read.All. Detail: {Detail}", tenantId, detail.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get sign-in detail for tenant {TenantId}", tenantId);
        }

        var snapshots = buckets.Select(kvp => new SignInDetailSnapshot
        {
            TenantId = tenantId,
            ReportDate = kvp.Key.Day,
            ClientApp = kvp.Key.ClientApp,
            Country = kvp.Key.Country,
            IsLegacyAuth = kvp.Value.Legacy,
            SuccessCount = kvp.Value.Success,
            FailureCount = kvp.Value.Failure,
            RiskyCount = kvp.Value.Risky,
            CollectedAt = DateTime.UtcNow
        }).ToList();

        return (snapshots, maxCreated);
    }

    // Modern-auth clients are "Browser" and "Mobile Apps and Desktop clients"; every other
    // clientAppUsed value (IMAP4, POP3, SMTP, Exchange ActiveSync, Other clients, etc.) is a
    // legacy-auth protocol that bypasses modern auth and most MFA / Conditional Access.
    private static bool IsLegacyAuthClient(string clientApp) =>
        !clientApp.Equals("Browser", StringComparison.OrdinalIgnoreCase) &&
        !clientApp.Equals("Mobile Apps and Desktop clients", StringComparison.OrdinalIgnoreCase) &&
        !clientApp.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    private static bool IsRiskySignIn(Microsoft.Graph.Beta.Models.RiskLevel? level) =>
        level is Microsoft.Graph.Beta.Models.RiskLevel.Low
            or Microsoft.Graph.Beta.Models.RiskLevel.Medium
            or Microsoft.Graph.Beta.Models.RiskLevel.High;

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

}
