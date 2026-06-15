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

    // High-risk delegated/application scopes that warrant attention when consented to a third-party app.
    private static readonly HashSet<string> HighRiskOAuthScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mail.Read", "Mail.ReadWrite", "Mail.Send", "MailboxSettings.ReadWrite",
        "Files.Read.All", "Files.ReadWrite.All", "Sites.Read.All", "Sites.ReadWrite.All", "Sites.FullControl.All",
        "Directory.Read.All", "Directory.ReadWrite.All", "User.Read.All", "User.ReadWrite.All",
        "Group.ReadWrite.All", "Application.ReadWrite.All", "RoleManagement.ReadWrite.Directory",
        "AppRoleAssignment.ReadWrite.All", "full_access_as_app", "Exchange.ManageAsApp",
        "Chat.Read.All", "ChannelMessage.Read.All", "Calendars.ReadWrite",
    };

    // Directory audit / change log — Microsoft Graph /auditLogs/directoryAudits.
    // Aggregates events (newer than the cursor) by (category, activity, day). Actor UPNs are
    // pseudonymized (never persisted) and only counted as DistinctActors. The newest activity
    // timestamp seen is returned so the caller can advance the per-tenant cursor.
    // Requires AuditLog.Read.All. Free-tier tenants retain only ~7 days of directory audit.
    public async Task<(List<DirectoryAuditSnapshot> Snapshots, DateTime? MaxActivityDateTime)> GetDirectoryAuditsAsync(string tenantId, DateTime? sinceUtc)
    {
        var client = CreateClientForTenant(tenantId);
        var since = sinceUtc ?? DateTime.UtcNow.AddDays(-7);
        // key: (category, activity, day) -> (events, failures, distinct pseudonymized actors)
        var buckets = new Dictionary<(string Category, string Activity, DateTime Day), (int Events, int Failures, HashSet<string> Actors)>();
        DateTime? maxActivity = null;

        try
        {
            var page = await client.AuditLogs.DirectoryAudits.GetAsync(c =>
            {
                c.QueryParameters.Filter = $"activityDateTime gt {since:yyyy-MM-ddTHH:mm:ssZ}";
                c.QueryParameters.Top = 999;
                c.QueryParameters.Orderby = ["activityDateTime"];
            });

            if (page?.Value != null)
            {
                void Accumulate(Microsoft.Graph.Models.DirectoryAudit a)
                {
                    var when = a.ActivityDateTime?.UtcDateTime;
                    if (when == null) return;
                    if (maxActivity == null || when > maxActivity) maxActivity = when;

                    var category = string.IsNullOrEmpty(a.Category) ? "Other" : a.Category;
                    var activity = string.IsNullOrEmpty(a.ActivityDisplayName) ? "Unknown" : a.ActivityDisplayName;
                    var day = when.Value.Date;
                    var key = (category, activity, day);
                    if (!buckets.TryGetValue(key, out var agg))
                        agg = (0, 0, new HashSet<string>(StringComparer.Ordinal));

                    agg.Events++;
                    var isFailure = a.Result == Microsoft.Graph.Models.OperationResult.Failure;
                    if (isFailure) agg.Failures++;

                    var actor = a.InitiatedBy?.User?.UserPrincipalName ?? a.InitiatedBy?.App?.DisplayName;
                    if (!string.IsNullOrEmpty(actor))
                        agg.Actors.Add(Pseudonymize(actor, tenantId));

                    buckets[key] = agg;
                }

                var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.DirectoryAudit, Microsoft.Graph.Models.DirectoryAuditCollectionResponse>
                    .CreatePageIterator(client, page, a => { Accumulate(a); return true; });
                await iterator.IterateAsync();
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Directory audit data unavailable for tenant {TenantId}: insufficient permissions. Requires AuditLog.Read.All.", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get directory audits for tenant {TenantId}", tenantId);
        }

        var snapshots = buckets.Select(kvp => new DirectoryAuditSnapshot
        {
            TenantId = tenantId,
            ReportDate = kvp.Key.Day,
            Category = kvp.Key.Category,
            Activity = kvp.Key.Activity,
            EventCount = kvp.Value.Events,
            FailureCount = kvp.Value.Failures,
            DistinctActors = kvp.Value.Actors.Count,
            CollectedAt = DateTime.UtcNow
        }).ToList();

        return (snapshots, maxActivity);
    }

    // License assignment errors & seat waste — Microsoft Graph /users.
    // Per SKU: counts users whose license assignment is in an Error state, and users who are
    // disabled (accountEnabled == false) but still hold the license (wasted seats).
    // Requires User.Read.All. SKU GUID -> part number is resolved from the supplied license snapshots.
    public async Task<List<LicenseAssignmentIssueSnapshot>> GetLicenseAssignmentIssuesAsync(string tenantId, IEnumerable<LicenseSnapshot> licenses)
    {
        var client = CreateClientForTenant(tenantId);
        var reportDate = DateTime.UtcNow.Date;
        var skuNames = licenses
            .Where(l => !string.IsNullOrEmpty(l.SkuId))
            .GroupBy(l => l.SkuId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.IsNullOrEmpty(g.First().SkuPartNumber) ? g.Key : g.First().SkuPartNumber, StringComparer.OrdinalIgnoreCase);

        // key: skuId -> (errorUsers, disabledLicensedUsers)
        var buckets = new Dictionary<string, (int Errors, int Disabled)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var page = await client.Users.GetAsync(c =>
            {
                c.QueryParameters.Select = ["id", "accountEnabled", "assignedLicenses", "licenseAssignmentStates"];
                c.QueryParameters.Top = 999;
            });

            if (page?.Value != null)
            {
                void Accumulate(Microsoft.Graph.Models.User u)
                {
                    var disabled = u.AccountEnabled == false;
                    if (u.LicenseAssignmentStates != null)
                    {
                        foreach (var state in u.LicenseAssignmentStates)
                        {
                            var skuId = state.SkuId?.ToString();
                            if (string.IsNullOrEmpty(skuId)) continue;
                            var isError = string.Equals(state.State, "Error", StringComparison.OrdinalIgnoreCase)
                                          || (!string.IsNullOrEmpty(state.Error) && !string.Equals(state.Error, "None", StringComparison.OrdinalIgnoreCase));
                            if (!isError && !disabled) continue;
                            var agg = buckets.TryGetValue(skuId, out var a) ? a : (0, 0);
                            if (isError) agg.Item1++;
                            if (disabled) agg.Item2++;
                            buckets[skuId] = agg;
                        }
                    }
                }

                var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.User, Microsoft.Graph.Models.UserCollectionResponse>
                    .CreatePageIterator(client, page, u => { Accumulate(u); return true; });
                await iterator.IterateAsync();
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("License assignment data unavailable for tenant {TenantId}: insufficient permissions. Requires User.Read.All.", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get license assignment issues for tenant {TenantId}", tenantId);
        }

        return buckets
            .Where(kvp => kvp.Value.Errors > 0 || kvp.Value.Disabled > 0)
            .Select(kvp => new LicenseAssignmentIssueSnapshot
            {
                TenantId = tenantId,
                ReportDate = reportDate,
                SkuId = kvp.Key,
                SkuPartNumber = skuNames.TryGetValue(kvp.Key, out var name) ? name : kvp.Key,
                ErrorUsers = kvp.Value.Errors,
                DisabledLicensedUsers = kvp.Value.Disabled,
                CollectedAt = DateTime.UtcNow
            }).ToList();
    }

    // OAuth app consent grants — Microsoft Graph /oauth2PermissionGrants (delegated) + /servicePrincipals.
    // One row per (app, grant type). Flags high-risk scopes and admin-consented grants so admins
    // can spot over-privileged third-party apps. Requires Application.Read.All + Directory.Read.All.
    public async Task<List<OAuthGrantSnapshot>> GetOAuthGrantsAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var reportDate = DateTime.UtcNow.Date;
        var result = new List<OAuthGrantSnapshot>();

        try
        {
            // Map servicePrincipal objectId -> (appId, displayName) for delegated grant resolution.
            var spInfo = new Dictionary<string, (string AppId, string DisplayName)>(StringComparer.OrdinalIgnoreCase);
            var spPage = await client.ServicePrincipals.GetAsync(c =>
            {
                c.QueryParameters.Select = ["id", "appId", "displayName"];
                c.QueryParameters.Top = 999;
            });
            if (spPage?.Value != null)
            {
                void AccumulateSp(Microsoft.Graph.Models.ServicePrincipal sp)
                {
                    if (!string.IsNullOrEmpty(sp.Id))
                        spInfo[sp.Id] = (sp.AppId ?? string.Empty, sp.DisplayName ?? "Unknown app");
                }
                var spIterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.ServicePrincipal, Microsoft.Graph.Models.ServicePrincipalCollectionResponse>
                    .CreatePageIterator(client, spPage, sp => { AccumulateSp(sp); return true; });
                await spIterator.IterateAsync();
            }

            // Delegated grants (oauth2PermissionGrants): aggregate scopes per client service principal.
            var delegatedScopes = new Dictionary<string, (HashSet<string> Scopes, bool AllPrincipals)>(StringComparer.OrdinalIgnoreCase);
            var grantPage = await client.Oauth2PermissionGrants.GetAsync(c => { c.QueryParameters.Top = 999; });
            if (grantPage?.Value != null)
            {
                void AccumulateGrant(Microsoft.Graph.Models.OAuth2PermissionGrant g)
                {
                    var clientId = g.ClientId;
                    if (string.IsNullOrEmpty(clientId)) return;
                    if (!delegatedScopes.TryGetValue(clientId, out var entry))
                        entry = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);
                    foreach (var s in (g.Scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        entry.Scopes.Add(s);
                    if (string.Equals(g.ConsentType, "AllPrincipals", StringComparison.OrdinalIgnoreCase))
                        entry.AllPrincipals = true;
                    delegatedScopes[clientId] = entry;
                }
                var grantIterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.OAuth2PermissionGrant, Microsoft.Graph.Models.OAuth2PermissionGrantCollectionResponse>
                    .CreatePageIterator(client, grantPage, g => { AccumulateGrant(g); return true; });
                await grantIterator.IterateAsync();
            }

            foreach (var (clientId, entry) in delegatedScopes)
            {
                spInfo.TryGetValue(clientId, out var info);
                var risky = entry.Scopes.Where(s => HighRiskOAuthScopes.Contains(s)).OrderBy(s => s).ToList();
                result.Add(new OAuthGrantSnapshot
                {
                    TenantId = tenantId,
                    ReportDate = reportDate,
                    AppDisplayName = info.DisplayName ?? "Unknown app",
                    AppId = info.AppId ?? clientId,
                    GrantType = "Delegated",
                    HighRiskScopes = string.Join(",", risky),
                    ScopeCount = entry.Scopes.Count,
                    IsAdminConsented = entry.AllPrincipals,
                    CollectedAt = DateTime.UtcNow
                });
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("OAuth grant data unavailable for tenant {TenantId}: insufficient permissions. Requires Application.Read.All + Directory.Read.All.", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get OAuth grants for tenant {TenantId}", tenantId);
        }

        return result;
    }
}
