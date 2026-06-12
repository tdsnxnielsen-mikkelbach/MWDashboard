using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using MWDashboard.Shared.Models;
using BetaGraphClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace MWDashboard.Shared.Services;

public partial class GraphReportService
{
    private static readonly HashSet<string> PrivilegedRoleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Global Administrator", "Privileged Role Administrator", "Privileged Authentication Administrator",
        "Security Administrator", "Exchange Administrator", "SharePoint Administrator", "User Administrator",
        "Application Administrator", "Cloud Application Administrator", "Conditional Access Administrator",
        "Authentication Administrator", "Intune Administrator", "Hybrid Identity Administrator",
        "Helpdesk Administrator", "Billing Administrator", "Global Reader",
    };

    public async Task<List<AppCredentialSnapshot>> GetAppCredentialsAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var result = new List<AppCredentialSnapshot>();
        var reportDate = DateTime.UtcNow.Date;
        var now = DateTime.UtcNow;

        void AddCredential(string appId, string objectId, string displayName, string credType, Guid? keyId, string? credName, DateTimeOffset? endDateTime)
        {
            if (endDateTime == null) return;
            var end = endDateTime.Value.UtcDateTime;
            result.Add(new AppCredentialSnapshot
            {
                TenantId = tenantId,
                ReportDate = reportDate,
                AppId = appId,
                AppObjectId = objectId,
                AppDisplayName = displayName,
                CredentialType = credType,
                KeyId = keyId?.ToString() ?? Guid.NewGuid().ToString(),
                DisplayName = credName ?? string.Empty,
                EndDateTime = end,
                DaysToExpiry = (int)Math.Floor((end - now).TotalDays),
                IsExpired = end < now,
                CollectedAt = DateTime.UtcNow
            });
        }

        try
        {
            var page = await client.Applications.GetAsync(c =>
            {
                c.QueryParameters.Select = ["id", "appId", "displayName", "passwordCredentials", "keyCredentials"];
                c.QueryParameters.Top = 999;
            });

            if (page?.Value != null)
            {
                void Accumulate(Microsoft.Graph.Models.Application a)
                {
                    var appId = a.AppId ?? string.Empty;
                    var objectId = a.Id ?? string.Empty;
                    var name = a.DisplayName ?? string.Empty;
                    if (a.PasswordCredentials != null)
                        foreach (var p in a.PasswordCredentials)
                            AddCredential(appId, objectId, name, "Secret", p.KeyId, p.DisplayName, p.EndDateTime);
                    if (a.KeyCredentials != null)
                        foreach (var k in a.KeyCredentials)
                            AddCredential(appId, objectId, name, "Certificate", k.KeyId, k.DisplayName, k.EndDateTime);
                }

                var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.Application, Microsoft.Graph.Models.ApplicationCollectionResponse>
                    .CreatePageIterator(client, page, a => { Accumulate(a); return true; });
                await iterator.IterateAsync();
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("App credential data unavailable for tenant {TenantId}: insufficient permissions. Requires Application.Read.All.", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get app credentials for tenant {TenantId}", tenantId);
        }

        return result;
    }

    // Privileged role inventory — one row per activated Entra directory role with its standing member count.
    // Requires RoleManagement.Read.Directory.
    public async Task<List<PrivilegedRoleSnapshot>> GetPrivilegedRolesAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var result = new List<PrivilegedRoleSnapshot>();
        var reportDate = DateTime.UtcNow.Date;

        try
        {
            var page = await client.DirectoryRoles.GetAsync(c =>
            {
                c.QueryParameters.Expand = ["members($select=id)"];
            });

            if (page?.Value != null)
            {
                void Accumulate(Microsoft.Graph.Models.DirectoryRole r)
                {
                    var name = r.DisplayName ?? string.Empty;
                    if (string.IsNullOrEmpty(name)) return;
                    result.Add(new PrivilegedRoleSnapshot
                    {
                        TenantId = tenantId,
                        ReportDate = reportDate,
                        RoleName = name,
                        RoleTemplateId = r.RoleTemplateId ?? string.Empty,
                        MemberCount = r.Members?.Count ?? 0,
                        IsPrivileged = PrivilegedRoleNames.Contains(name),
                        CollectedAt = DateTime.UtcNow
                    });
                }

                var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.DirectoryRole, Microsoft.Graph.Models.DirectoryRoleCollectionResponse>
                    .CreatePageIterator(client, page, r => { Accumulate(r); return true; });
                await iterator.IterateAsync();
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Privileged role data unavailable for tenant {TenantId}: insufficient permissions. Requires RoleManagement.Read.Directory.", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get privileged roles for tenant {TenantId}", tenantId);
        }

        return result;
    }

    // Microsoft Defender / M365 security alerts — daily aggregate by severity + status (last 30 days).
    // Requires SecurityAlert.Read.All.
    public async Task<List<DefenderAlertSnapshot>> GetDefenderAlertsAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var reportDate = DateTime.UtcNow.Date;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var buckets = new Dictionary<(string Severity, string Status), int>();

        try
        {
            var page = await client.Security.Alerts_v2.GetAsync(c =>
            {
                c.QueryParameters.Filter = $"createdDateTime ge {cutoff:yyyy-MM-ddTHH:mm:ssZ}";
                c.QueryParameters.Top = 999;
            });

            if (page?.Value != null)
            {
                void Accumulate(Microsoft.Graph.Models.Security.Alert a)
                {
                    var severity = a.Severity?.ToString() ?? "unknown";
                    var status = a.Status?.ToString() ?? "unknown";
                    var key = (severity, status);
                    buckets[key] = buckets.TryGetValue(key, out var n) ? n + 1 : 1;
                }

                var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.Security.Alert, Microsoft.Graph.Models.Security.AlertCollectionResponse>
                    .CreatePageIterator(client, page, a => { Accumulate(a); return true; });
                await iterator.IterateAsync();
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Defender alert data unavailable for tenant {TenantId}: insufficient permissions. Requires SecurityAlert.Read.All.", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Defender alerts for tenant {TenantId}", tenantId);
        }

        return buckets.Select(kvp => new DefenderAlertSnapshot
        {
            TenantId = tenantId,
            ReportDate = reportDate,
            Severity = kvp.Key.Severity,
            Status = kvp.Key.Status,
            AlertCount = kvp.Value,
            CollectedAt = DateTime.UtcNow
        }).ToList();
    }
}
