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
    Task<(List<SecureScoreSnapshot> Scores, List<SecureScoreControlSnapshot> Controls)> GetSecureScoreAsync(string tenantId);
    Task<MfaRegistrationSnapshot?> GetMfaRegistrationAsync(string tenantId);
    Task<InactiveAccountSnapshot?> GetInactiveAccountsAsync(string tenantId);
    Task<(List<ServiceHealthSnapshot> Services, List<ServiceHealthIssueSnapshot> Issues)> GetServiceHealthAsync(string tenantId);
    Task<DeviceComplianceSnapshot?> GetDeviceComplianceAsync(string tenantId);
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
}
