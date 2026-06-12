using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using MWDashboard.Shared.Models;
using BetaGraphClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace MWDashboard.Shared.Services;

public partial class GraphReportService
{
    public async Task<(List<ServiceHealthSnapshot> Services, List<ServiceHealthIssueSnapshot> Issues)> GetServiceHealthAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var services = new List<ServiceHealthSnapshot>();
        var issues = new List<ServiceHealthIssueSnapshot>();
        var reportDate = DateTime.UtcNow.Date;

        try
        {
            // Per-service health overview
            var overviews = await client.Admin.ServiceAnnouncement.HealthOverviews.GetAsync();
            if (overviews?.Value != null)
            {
                foreach (var o in overviews.Value)
                {
                    if (string.IsNullOrEmpty(o.Service)) continue;
                    services.Add(new ServiceHealthSnapshot
                    {
                        TenantId = tenantId,
                        ReportDate = reportDate,
                        ServiceName = o.Service,
                        Status = o.Status?.ToString() ?? "Unknown",
                        CollectedAt = DateTime.UtcNow
                    });
                }
            }

            // Active service issues (incidents + advisories)
            var issuePage = await client.Admin.ServiceAnnouncement.Issues.GetAsync(config =>
            {
                config.QueryParameters.Top = 100;
            });

            if (issuePage?.Value != null)
            {
                void Accumulate(Microsoft.Graph.Models.ServiceHealthIssue i)
                {
                    if (string.IsNullOrEmpty(i.Id)) return;
                    // Only keep unresolved issues — resolved ones aren't actionable
                    if (i.IsResolved == true) return;

                    issues.Add(new ServiceHealthIssueSnapshot
                    {
                        TenantId = tenantId,
                        ReportDate = reportDate,
                        IssueId = i.Id,
                        Title = i.Title ?? string.Empty,
                        ServiceName = i.Service ?? string.Empty,
                        Classification = i.Classification?.ToString() ?? string.Empty,
                        Status = i.Status?.ToString() ?? string.Empty,
                        Feature = i.Feature ?? string.Empty,
                        StartDateTime = i.StartDateTime?.UtcDateTime,
                        IsResolved = i.IsResolved ?? false,
                        CollectedAt = DateTime.UtcNow
                    });
                }

                var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.ServiceHealthIssue, Microsoft.Graph.Models.ServiceHealthIssueCollectionResponse>
                    .CreatePageIterator(client, issuePage, i => { Accumulate(i); return true; });
                await iterator.IterateAsync();
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Service health data unavailable for tenant {TenantId}: insufficient permissions. " +
                "Requires ServiceHealth.Read.All.", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get service health for tenant {TenantId}", tenantId);
        }

        return (services, issues);
    }

    // Intune device compliance — tenant-level point-in-time counts of managed devices by
    // compliance state and operating system. Requires DeviceManagementManagedDevices.Read.All.
    public async Task<DeviceComplianceSnapshot?> GetDeviceComplianceAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.DeviceManagement.ManagedDevices.GetAsync(c =>
            {
                c.QueryParameters.Select = ["id", "complianceState", "operatingSystem"];
                c.QueryParameters.Top = 999;
            });

            if (page?.Value == null) return null;

            var snapshot = new DeviceComplianceSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            void Accumulate(Microsoft.Graph.Models.ManagedDevice d)
            {
                snapshot.TotalDevices++;

                switch (d.ComplianceState)
                {
                    case Microsoft.Graph.Models.ComplianceState.Compliant:
                        snapshot.CompliantCount++; break;
                    case Microsoft.Graph.Models.ComplianceState.Noncompliant:
                        snapshot.NonCompliantCount++; break;
                    case Microsoft.Graph.Models.ComplianceState.InGracePeriod:
                        snapshot.InGracePeriodCount++; break;
                    case Microsoft.Graph.Models.ComplianceState.Error:
                        snapshot.ErrorCount++; break;
                    default:
                        snapshot.UnknownCount++; break;
                }

                var os = (d.OperatingSystem ?? string.Empty).ToLowerInvariant();
                if (os.Contains("windows")) snapshot.WindowsCount++;
                else if (os.Contains("ios") || os.Contains("ipados")) snapshot.IosCount++;
                else if (os.Contains("android")) snapshot.AndroidCount++;
                else if (os.Contains("mac")) snapshot.MacOsCount++;
                else snapshot.OtherOsCount++;
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.ManagedDevice, Microsoft.Graph.Models.ManagedDeviceCollectionResponse>
                .CreatePageIterator(client, page, d => { Accumulate(d); return true; });
            await iterator.IterateAsync();

            return snapshot;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Device compliance unavailable for tenant {TenantId}: insufficient permissions. " +
                "Requires DeviceManagementManagedDevices.Read.All.", tenantId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get device compliance for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // Conditional Access coverage — counts policies by state and detects whether key
    // protections (legacy-auth block, MFA grant) exist in any enabled policy.
    // Requires Policy.Read.All.
    public async Task<ConditionalAccessSnapshot?> GetConditionalAccessAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.Identity.ConditionalAccess.Policies.GetAsync();
            if (page?.Value == null) return null;

            var snapshot = new ConditionalAccessSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            foreach (var p in page.Value)
            {
                snapshot.TotalPolicies++;
                var enabled = p.State == Microsoft.Graph.Models.ConditionalAccessPolicyState.Enabled;
                switch (p.State)
                {
                    case Microsoft.Graph.Models.ConditionalAccessPolicyState.Enabled:
                        snapshot.EnabledPolicies++; break;
                    case Microsoft.Graph.Models.ConditionalAccessPolicyState.EnabledForReportingButNotEnforced:
                        snapshot.ReportOnlyPolicies++; break;
                    default:
                        snapshot.DisabledPolicies++; break;
                }

                if (!enabled) continue;

                // MFA grant control present?
                var controls = p.GrantControls?.BuiltInControls;
                if (controls != null && controls.Contains(Microsoft.Graph.Models.ConditionalAccessGrantControl.Mfa))
                    snapshot.RequiresMfa = true;

                // Legacy-auth block: a block policy targeting the legacy client app types
                var clientApps = p.Conditions?.ClientAppTypes;
                var targetsLegacy = clientApps != null && (
                    clientApps.Contains(Microsoft.Graph.Models.ConditionalAccessClientApp.ExchangeActiveSync) ||
                    clientApps.Contains(Microsoft.Graph.Models.ConditionalAccessClientApp.Other));
                var blocks = controls != null && controls.Contains(Microsoft.Graph.Models.ConditionalAccessGrantControl.Block);
                if (targetsLegacy && blocks)
                    snapshot.BlocksLegacyAuth = true;
            }

            return snapshot;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Conditional Access data unavailable for tenant {TenantId}: insufficient permissions. " +
                "Requires Policy.Read.All.", tenantId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Conditional Access policies for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // Guest / external users — tenant-level governance counts. Uses User.Read.All (already
    // granted) and avoids signInActivity so it works on all license tiers.
    public async Task<GuestUserSnapshot?> GetGuestUsersAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.Users.GetAsync(c =>
            {
                c.QueryParameters.Filter = "userType eq 'Guest'";
                c.QueryParameters.Select = ["id", "externalUserState", "createdDateTime"];
                c.QueryParameters.Top = 999;
            });

            if (page?.Value == null) return null;

            var snapshot = new GuestUserSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            var recentCutoff = DateTimeOffset.UtcNow.AddDays(-30);

            void Accumulate(Microsoft.Graph.Models.User u)
            {
                snapshot.TotalGuests++;

                if (string.Equals(u.ExternalUserState, "PendingAcceptance", StringComparison.OrdinalIgnoreCase))
                    snapshot.PendingAcceptanceGuests++;
                else if (string.Equals(u.ExternalUserState, "Accepted", StringComparison.OrdinalIgnoreCase))
                    snapshot.AcceptedGuests++;

                if (u.CreatedDateTime != null && u.CreatedDateTime >= recentCutoff)
                    snapshot.RecentlyAddedGuests++;
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.User, Microsoft.Graph.Models.UserCollectionResponse>
                .CreatePageIterator(client, page, u => { Accumulate(u); return true; });
            await iterator.IterateAsync();

            return snapshot;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Guest user data unavailable for tenant {TenantId}: insufficient permissions. " +
                "Requires User.Read.All.", tenantId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get guest users for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // Risky users (Identity Protection) — counts at-risk users by risk level.
    // Requires IdentityRiskyUser.Read.All AND Entra ID P2 on the target tenant.
    public async Task<RiskyUserSnapshot?> GetRiskyUsersAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.IdentityProtection.RiskyUsers.GetAsync(c =>
            {
                c.QueryParameters.Select = ["id", "riskLevel", "riskState"];
                c.QueryParameters.Top = 999;
            });

            if (page?.Value == null) return null;

            var snapshot = new RiskyUserSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            void Accumulate(Microsoft.Graph.Models.RiskyUser r)
            {
                // Only count users still considered a risk
                var atRisk = r.RiskState == Microsoft.Graph.Models.RiskState.AtRisk
                    || r.RiskState == Microsoft.Graph.Models.RiskState.ConfirmedCompromised;
                if (!atRisk) return;

                snapshot.TotalAtRisk++;
                switch (r.RiskLevel)
                {
                    case Microsoft.Graph.Models.RiskLevel.High:
                        snapshot.HighRisk++; break;
                    case Microsoft.Graph.Models.RiskLevel.Medium:
                        snapshot.MediumRisk++; break;
                    case Microsoft.Graph.Models.RiskLevel.Low:
                        snapshot.LowRisk++; break;
                }
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.RiskyUser, Microsoft.Graph.Models.RiskyUserCollectionResponse>
                .CreatePageIterator(client, page, r => { Accumulate(r); return true; });
            await iterator.IterateAsync();

            return snapshot;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Risky user data unavailable for tenant {TenantId}: insufficient permissions or license. " +
                "Requires IdentityRiskyUser.Read.All + Entra ID P2.", tenantId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get risky users for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // ---- Tier 3: Usage & Governance ----

    private const int TopN = 20;

    // Mailbox usage — tenant aggregate (detail + quota-status counts) plus top-N largest mailboxes.
}
