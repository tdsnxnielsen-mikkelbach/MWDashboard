using System.Collections.Concurrent;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using MWDashboard.Shared.Models;
using BetaGraphClient = Microsoft.Graph.Beta.GraphServiceClient;

namespace MWDashboard.Shared.Services;

public interface IGraphReportService
{
    Task<List<MauSnapshot>> GetActiveUserCountsAsync(string tenantId, int periodDays = 180);
    Task<List<LicenseSnapshot>> GetSubscribedSkusAsync(string tenantId);
    Task<List<MessageCenterPost>> GetMessageCenterPostsAsync(string tenantId);
    Task<List<SecuritySignInSummary>> GetSignInSummaryAsync(string tenantId, int days = 30);
    Task<List<WorkloadActivitySnapshot>> GetWorkloadActivityAsync(string tenantId, int periodDays = 30);
    Task<List<CopilotUsageSnapshot>> GetCopilotUsageAsync(string tenantId);
    Task<List<UserSegmentSnapshot>> GetUserSegmentationAsync(string tenantId);
    Task<List<DepartmentUsageSnapshot>> GetDepartmentUsageAsync(string tenantId);
    Task<List<StorageSnapshot>> GetStorageUsageAsync(string tenantId);
    Task<List<M365AppUsageSnapshot>> GetM365AppUsageAsync(string tenantId);
    Task<List<M365AppUserDetailSnapshot>> GetM365AppUserDetailAsync(string tenantId);
    Task<(List<Office365ActivationSnapshot> Counts, List<Office365ActivationUserSnapshot> Users)> GetOffice365ActivationsAsync(string tenantId);
    Task<(List<SecureScoreSnapshot> Scores, List<SecureScoreControlSnapshot> Controls)> GetSecureScoreAsync(string tenantId);
    Task<MfaRegistrationSnapshot?> GetMfaRegistrationAsync(string tenantId);
    Task<InactiveAccountSnapshot?> GetInactiveAccountsAsync(string tenantId);
    Task<(List<ServiceHealthSnapshot> Services, List<ServiceHealthIssueSnapshot> Issues)> GetServiceHealthAsync(string tenantId);
    Task<(DeviceComplianceSnapshot? Compliance, List<DevicePatchSnapshot> Patch)> GetDeviceComplianceAsync(string tenantId);
    Task<ConditionalAccessSnapshot?> GetConditionalAccessAsync(string tenantId);
    Task<GuestUserSnapshot?> GetGuestUsersAsync(string tenantId);
    Task<RiskyUserSnapshot?> GetRiskyUsersAsync(string tenantId);
    Task<(MailboxUsageSnapshot? Aggregate, List<TopMailboxSnapshot> Top)> GetMailboxUsageAsync(string tenantId);
    Task<TeamsDeviceUsageSnapshot?> GetTeamsDeviceUsageAsync(string tenantId);
    Task<(List<SiteUsageSnapshot> Aggregates, List<SiteUsageDetailSnapshot> Details)> GetSiteUsageAsync(string tenantId);
    Task<YammerActivitySnapshot?> GetYammerActivityAsync(string tenantId);
    Task<GroupSnapshot?> GetGroupSprawlAsync(string tenantId);
    Task<List<AppCredentialSnapshot>> GetAppCredentialsAsync(string tenantId);
    Task<List<PrivilegedRoleSnapshot>> GetPrivilegedRolesAsync(string tenantId);
    Task<List<DefenderAlertSnapshot>> GetDefenderAlertsAsync(string tenantId);
    Task<List<SubscriptionSnapshot>> GetDirectorySubscriptionsAsync(string tenantId);
    Task<List<TeamsTeamActivitySnapshot>> GetTeamsTeamActivityAsync(string tenantId);
    Task<(List<DirectoryAuditSnapshot> Snapshots, DateTime? MaxActivityDateTime)> GetDirectoryAuditsAsync(string tenantId, DateTime? sinceUtc);
    Task<List<LicenseAssignmentIssueSnapshot>> GetLicenseAssignmentIssuesAsync(string tenantId, IEnumerable<LicenseSnapshot> licenses);
    Task<List<OAuthGrantSnapshot>> GetOAuthGrantsAsync(string tenantId);
    Task<List<string>> CheckMissingPermissionsAsync(string tenantId);

    /// <summary>
    /// Returns the set of user principal names (lower-cased) that hold an assigned Microsoft 365
    /// Copilot license. Used to split Copilot-Chat audit activity into licensed vs. unlicensed.
    /// Returns an empty set if the tenant has no Copilot SKU (so all chat users are unlicensed).
    /// </summary>
    Task<HashSet<string>> GetCopilotLicensedUpnsAsync(string tenantId);
}

public partial class GraphReportService : IGraphReportService
{
    private readonly IConfiguration _config;
    private readonly ILogger<GraphReportService> _logger;

    // Per-tenant caches. The OAuth token cache lives on the ClientSecretCredential instance, so
    // reusing one credential (and the clients built from it) across a tenant's collection run
    // avoids re-acquiring the same token ~27 times. The service is registered Scoped, so these
    // caches live only for the duration of a single request (collector) or one-shot run (job).
    private readonly ConcurrentDictionary<string, ClientSecretCredential> _credentialCache = new();
    private readonly ConcurrentDictionary<string, GraphServiceClient> _clientCache = new();
    private readonly ConcurrentDictionary<string, BetaGraphClient> _betaClientCache = new();

    public GraphReportService(IConfiguration config, ILogger<GraphReportService> logger)
    {
        _config = config;
        _logger = logger;
    }

    // Salt for pseudonymizing PII (UPNs, display names) in per-user reports. Prefers an explicit
    // Anonymization:Salt; falls back to the always-present client secret so tokens are still
    // deterministic-per-deployment and non-reversible without the secret.
    private string AnonymizationSalt =>
        _config["Anonymization:Salt"]
        ?? _config["AzureAd:ClientSecret"]
        ?? "mwdashboard";

    /// <summary>Tenant-scoped, non-reversible pseudonym for a user identifier (UPN / display name).</summary>
    private string Pseudonymize(string? value, string tenantId)
        => PiiProtector.Pseudonymize(value, tenantId, AnonymizationSalt);

    private ClientSecretCredential GetCredentialForTenant(string tenantId)
        => _credentialCache.GetOrAdd(tenantId, tid => new ClientSecretCredential(
            tid,
            _config["AzureAd:ClientId"],
            _config["AzureAd:ClientSecret"]));

    private GraphServiceClient CreateClientForTenant(string tenantId)
        => _clientCache.GetOrAdd(tenantId, tid =>
            new GraphServiceClient(GetCredentialForTenant(tid), ["https://graph.microsoft.com/.default"]));

    private BetaGraphClient CreateBetaClientForTenant(string tenantId)
        => _betaClientCache.GetOrAdd(tenantId, tid =>
            new BetaGraphClient(GetCredentialForTenant(tid), ["https://graph.microsoft.com/.default"]));
}
