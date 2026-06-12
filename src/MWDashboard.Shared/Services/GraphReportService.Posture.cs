using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using MWDashboard.Shared.Models;
using BetaGraphClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace MWDashboard.Shared.Services;

public partial class GraphReportService
{
    public async Task<(List<SecureScoreSnapshot> Scores, List<SecureScoreControlSnapshot> Controls)> GetSecureScoreAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);
        var scores = new List<SecureScoreSnapshot>();
        var controls = new List<SecureScoreControlSnapshot>();

        try
        {
            // Returns up to 90 days of daily scores, sorted latest-first
            var result = await client.Security.SecureScores.GetAsync(config =>
            {
                config.QueryParameters.Top = 90;
            });

            var dailyScores = result?.Value;
            if (dailyScores == null || dailyScores.Count == 0)
                return (scores, controls);

            foreach (var s in dailyScores)
            {
                if (!s.CreatedDateTime.HasValue) continue;

                var comparative = s.AverageComparativeScores?
                    .FirstOrDefault(a => string.Equals(a.Basis, "AllTenants", StringComparison.OrdinalIgnoreCase))?
                    .AverageScore ?? 0;

                scores.Add(new SecureScoreSnapshot
                {
                    TenantId = tenantId,
                    ReportDate = s.CreatedDateTime.Value.UtcDateTime.Date,
                    CurrentScore = s.CurrentScore ?? 0,
                    MaxScore = s.MaxScore ?? 0,
                    ActiveUserCount = s.ActiveUserCount ?? 0,
                    LicensedUserCount = s.LicensedUserCount ?? 0,
                    ComparativeScoreAllTenants = comparative,
                    CollectedAt = DateTime.UtcNow
                });
            }

            // Per-control remediation data comes from the most recent score
            var latest = dailyScores
                .Where(s => s.CreatedDateTime.HasValue)
                .OrderByDescending(s => s.CreatedDateTime!.Value)
                .FirstOrDefault();

            if (latest?.ControlScores != null && latest.CreatedDateTime.HasValue)
            {
                var reportDate = latest.CreatedDateTime.Value.UtcDateTime.Date;
                foreach (var c in latest.ControlScores)
                {
                    if (string.IsNullOrEmpty(c.ControlName)) continue;

                    // scoreInPercentage and implementationStatus are returned in AdditionalData
                    double scorePct = 0;
                    string implStatus = string.Empty;
                    if (c.AdditionalData != null)
                    {
                        if (c.AdditionalData.TryGetValue("scoreInPercentage", out var pctObj) && pctObj != null)
                            double.TryParse(pctObj.ToString(), out scorePct);
                        if (c.AdditionalData.TryGetValue("implementationStatus", out var statusObj) && statusObj != null)
                            implStatus = statusObj.ToString() ?? string.Empty;
                    }

                    controls.Add(new SecureScoreControlSnapshot
                    {
                        TenantId = tenantId,
                        ReportDate = reportDate,
                        ControlName = c.ControlName,
                        ControlCategory = c.ControlCategory ?? string.Empty,
                        Description = c.Description ?? string.Empty,
                        Score = c.Score ?? 0,
                        ScoreInPercentage = scorePct,
                        ImplementationStatus = implStatus,
                        CollectedAt = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Secure Score for tenant {TenantId}", tenantId);
        }

        return (scores, controls);
    }

    // MFA / authentication method registration — aggregated tenant-level counts (member users only, no PII stored)
    public async Task<MfaRegistrationSnapshot?> GetMfaRegistrationAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.Reports.AuthenticationMethods.UserRegistrationDetails.GetAsync(config =>
            {
                config.QueryParameters.Top = 999;
            });

            if (page?.Value == null)
                return null;

            var snapshot = new MfaRegistrationSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            void Accumulate(Microsoft.Graph.Models.UserRegistrationDetails d)
            {
                // Count member accounts only — guests aren't part of the org's MFA posture
                if (!string.Equals(d.UserType?.ToString(), "member", StringComparison.OrdinalIgnoreCase))
                    return;

                snapshot.TotalUsers++;
                if (d.IsMfaRegistered == true) snapshot.MfaRegistered++;
                if (d.IsMfaCapable == true) snapshot.MfaCapable++;
                if (d.IsPasswordlessCapable == true) snapshot.PasswordlessCapable++;
                if (d.IsSsprRegistered == true) snapshot.SsprRegistered++;
                if (d.IsSsprCapable == true) snapshot.SsprCapable++;
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.UserRegistrationDetails, Microsoft.Graph.Models.UserRegistrationDetailsCollectionResponse>
                .CreatePageIterator(client, page, d => { Accumulate(d); return true; });
            await iterator.IterateAsync();

            return snapshot.TotalUsers > 0 ? snapshot : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get MFA registration details for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // Inactive / stale licensed accounts — tenant-level staleness counts based on last interactive sign-in
    public async Task<InactiveAccountSnapshot?> GetInactiveAccountsAsync(string tenantId)
    {
        var client = CreateClientForTenant(tenantId);

        try
        {
            var page = await client.Users.GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "accountEnabled", "userType", "assignedLicenses", "signInActivity"];
                config.QueryParameters.Top = 999;
            });

            if (page?.Value == null)
                return null;

            var snapshot = new InactiveAccountSnapshot
            {
                TenantId = tenantId,
                ReportDate = DateTime.UtcNow.Date,
                CollectedAt = DateTime.UtcNow
            };

            var now = DateTimeOffset.UtcNow;

            void Accumulate(Microsoft.Graph.Models.User u)
            {
                // Only enabled, licensed member accounts count toward license-waste analysis
                if (u.AccountEnabled != true)
                    return;
                if (!string.Equals(u.UserType, "Member", StringComparison.OrdinalIgnoreCase))
                    return;
                if (u.AssignedLicenses == null || u.AssignedLicenses.Count == 0)
                    return;

                snapshot.TotalLicensedUsers++;

                var lastSignIn = u.SignInActivity?.LastSignInDateTime;
                if (lastSignIn == null)
                {
                    snapshot.NeverSignedIn++;
                    snapshot.Inactive30++;
                    snapshot.Inactive60++;
                    snapshot.Inactive90++;
                    return;
                }

                var daysInactive = (now - lastSignIn.Value).TotalDays;
                if (daysInactive >= 30) snapshot.Inactive30++;
                if (daysInactive >= 60) snapshot.Inactive60++;
                if (daysInactive >= 90) snapshot.Inactive90++;
            }

            var iterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.User, Microsoft.Graph.Models.UserCollectionResponse>
                .CreatePageIterator(client, page, u => { Accumulate(u); return true; });
            await iterator.IterateAsync();

            return snapshot.TotalLicensedUsers > 0 ? snapshot : null;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.ResponseStatusCode == 403)
        {
            // The signInActivity property is gated behind a Microsoft Entra ID P1/P2 license.
            // A tenant on the free tier returns 403 even when AuditLog.Read.All + User.Read.All
            // are fully consented, so distinguish that case from a genuine consent gap.
            var detail = $"{odataEx.Error?.Code} {odataEx.Error?.Message} {odataEx.Message}";
            if (IsPremiumLicenseError(detail))
            {
                _logger.LogWarning("Inactive account data unavailable for tenant {TenantId}: reading signInActivity " +
                    "requires a Microsoft Entra ID P1/P2 license (permissions are consented). Detail: {Detail}",
                    tenantId, detail.Trim());
            }
            else
            {
                _logger.LogWarning("Inactive account data unavailable for tenant {TenantId}: insufficient permissions. " +
                    "Requires AuditLog.Read.All + User.Read.All. Detail: {Detail}", tenantId, detail.Trim());
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get inactive account details for tenant {TenantId}", tenantId);
            return null;
        }
    }

    // signInActivity (and several other sign-in-derived properties) require an Entra ID P1/P2 license.
    // Graph signals this with a 403 whose body mentions premium/license rather than a missing permission.
    private static bool IsPremiumLicenseError(string detail)
    {
        if (string.IsNullOrEmpty(detail)) return false;
        return detail.Contains("premium", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("P1", StringComparison.Ordinal)
            || detail.Contains("P2", StringComparison.Ordinal)
            || detail.Contains("Aad Premium", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("does not have a valid license", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("RequestFromNonPremiumTenant", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("B2C", StringComparison.OrdinalIgnoreCase);
    }

    // Service Health — per-service status overview + active service issues (incidents/advisories)
}
