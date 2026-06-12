using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public interface IMauDataService
{
    Task<List<MauSnapshot>> GetMauHistoryAsync(string? tenantId = null, int months = 12);
    Task<List<MauSnapshot>> GetMauHistoryAsync(IEnumerable<string>? tenantIds, int months = 12);
    Task<List<MauSnapshot>> GetLatestMauByServiceAsync(string? tenantId = null);
    Task<List<MauSnapshot>> GetLatestMauByServiceAsync(IEnumerable<string>? tenantIds);
    Task SaveSnapshotsAsync(IEnumerable<MauSnapshot> snapshots);
    Task<List<LicenseSnapshot>> GetLatestLicensesAsync(string? tenantId = null);
    Task<List<LicenseSnapshot>> GetLatestLicensesAsync(IEnumerable<string>? tenantIds);
    Task<List<LicenseSnapshot>> GetLicensesByDateRangeAsync(DateTime from, DateTime to, string? tenantId = null);
    Task<List<LicenseSnapshot>> GetLicensesByDateRangeAsync(DateTime from, DateTime to, IEnumerable<string>? tenantIds);
    Task SaveLicensesAsync(IEnumerable<LicenseSnapshot> licenses);
    Task<(DateTime? Earliest, DateTime? Latest)> GetLicenseDataRangeAsync();
    Task<List<MessageCenterPost>> GetMessageCenterPostsAsync(string? tenantId = null);
    Task<List<MessageCenterPost>> GetMessageCenterPostsAsync(IEnumerable<string>? tenantIds);
    Task SaveMessageCenterPostsAsync(IEnumerable<MessageCenterPost> posts);
    Task<List<SecuritySignInSummary>> GetSecuritySummaryAsync(string? tenantId = null, int days = 30);
    Task<List<SecuritySignInSummary>> GetSecuritySummaryAsync(IEnumerable<string>? tenantIds, int days = 30);
    Task SaveSecuritySummariesAsync(IEnumerable<SecuritySignInSummary> summaries);
    Task<List<TenantEntraTier>> GetTenantEntraIdTiersAsync();

    // Workload Activity
    Task<List<WorkloadActivitySnapshot>> GetWorkloadActivityAsync(IEnumerable<string>? tenantIds, int days = 30);
    Task SaveWorkloadActivityAsync(IEnumerable<WorkloadActivitySnapshot> activities);

    // Copilot Usage
    Task<List<CopilotUsageSnapshot>> GetCopilotUsageAsync(IEnumerable<string>? tenantIds);
    Task SaveCopilotUsageAsync(IEnumerable<CopilotUsageSnapshot> snapshots);

    // User Segmentation
    Task<List<UserSegmentSnapshot>> GetUserSegmentsAsync(IEnumerable<string>? tenantIds, int months = 6);
    Task SaveUserSegmentsAsync(IEnumerable<UserSegmentSnapshot> segments);

    // Department Usage
    Task<List<DepartmentUsageSnapshot>> GetDepartmentUsageAsync(IEnumerable<string>? tenantIds);
    Task SaveDepartmentUsageAsync(IEnumerable<DepartmentUsageSnapshot> snapshots);

    // Storage Usage
    Task<List<StorageSnapshot>> GetStorageAsync(IEnumerable<string>? tenantIds, int days = 30);
    Task SaveStorageAsync(IEnumerable<StorageSnapshot> snapshots);

    // Consumption Scores
    Task<List<ConsumptionSnapshot>> GetConsumptionAsync(IEnumerable<string>? tenantIds, int months = 6);
    Task SaveConsumptionAsync(ConsumptionSnapshot snapshot);

    // M365 App Platform Usage
    Task<List<M365AppUsageSnapshot>> GetM365AppUsageAsync(IEnumerable<string>? tenantIds);
    Task SaveM365AppUsageAsync(IEnumerable<M365AppUsageSnapshot> snapshots);

    // Secure Score
    Task<List<SecureScoreSnapshot>> GetSecureScoresAsync(IEnumerable<string>? tenantIds, int days = 90);
    Task SaveSecureScoresAsync(IEnumerable<SecureScoreSnapshot> snapshots);
    Task<List<SecureScoreControlSnapshot>> GetSecureScoreControlsAsync(IEnumerable<string>? tenantIds);
    Task SaveSecureScoreControlsAsync(IEnumerable<SecureScoreControlSnapshot> snapshots);

    // MFA Registration
    Task<List<MfaRegistrationSnapshot>> GetMfaRegistrationAsync(IEnumerable<string>? tenantIds);
    Task SaveMfaRegistrationAsync(MfaRegistrationSnapshot snapshot);

    // Inactive Accounts
    Task<List<InactiveAccountSnapshot>> GetInactiveAccountsAsync(IEnumerable<string>? tenantIds);
    Task SaveInactiveAccountsAsync(InactiveAccountSnapshot snapshot);

    // Service Health
    Task<List<ServiceHealthSnapshot>> GetServiceHealthAsync(IEnumerable<string>? tenantIds);
    Task SaveServiceHealthAsync(IEnumerable<ServiceHealthSnapshot> snapshots);
    Task<List<ServiceHealthIssueSnapshot>> GetServiceHealthIssuesAsync(IEnumerable<string>? tenantIds);
    Task SaveServiceHealthIssuesAsync(IEnumerable<ServiceHealthIssueSnapshot> snapshots);

    // Device Compliance (Intune)
    Task<List<DeviceComplianceSnapshot>> GetDeviceComplianceAsync(IEnumerable<string>? tenantIds);
    Task SaveDeviceComplianceAsync(DeviceComplianceSnapshot snapshot);

    // Conditional Access
    Task<List<ConditionalAccessSnapshot>> GetConditionalAccessAsync(IEnumerable<string>? tenantIds);
    Task SaveConditionalAccessAsync(ConditionalAccessSnapshot snapshot);

    // Guest Users
    Task<List<GuestUserSnapshot>> GetGuestUsersAsync(IEnumerable<string>? tenantIds);
    Task SaveGuestUsersAsync(GuestUserSnapshot snapshot);

    // Risky Users
    Task<List<RiskyUserSnapshot>> GetRiskyUsersAsync(IEnumerable<string>? tenantIds);
    Task SaveRiskyUsersAsync(RiskyUserSnapshot snapshot);

    // Mailbox Usage (Tier 3)
    Task<List<MailboxUsageSnapshot>> GetMailboxUsageAsync(IEnumerable<string>? tenantIds);
    Task SaveMailboxUsageAsync(MailboxUsageSnapshot snapshot);
    Task<List<TopMailboxSnapshot>> GetTopMailboxesAsync(IEnumerable<string>? tenantIds);
    Task SaveTopMailboxesAsync(IEnumerable<TopMailboxSnapshot> snapshots);

    // Teams Device Usage (Tier 3)
    Task<List<TeamsDeviceUsageSnapshot>> GetTeamsDeviceUsageAsync(IEnumerable<string>? tenantIds);
    Task SaveTeamsDeviceUsageAsync(TeamsDeviceUsageSnapshot snapshot);

    // SharePoint / OneDrive Site Usage (Tier 3)
    Task<List<SiteUsageSnapshot>> GetSiteUsageAsync(IEnumerable<string>? tenantIds);
    Task SaveSiteUsageAsync(IEnumerable<SiteUsageSnapshot> snapshots);
    Task<List<SiteUsageDetailSnapshot>> GetSiteUsageDetailAsync(IEnumerable<string>? tenantIds);
    Task SaveSiteUsageDetailAsync(IEnumerable<SiteUsageDetailSnapshot> snapshots);

    // Viva Engage / Yammer (Tier 3)
    Task<List<YammerActivitySnapshot>> GetYammerActivityAsync(IEnumerable<string>? tenantIds);
    Task SaveYammerActivityAsync(YammerActivitySnapshot snapshot);

    // Groups & Teams sprawl (Tier 3)
    Task<List<GroupSnapshot>> GetGroupSprawlAsync(IEnumerable<string>? tenantIds);
    Task SaveGroupSprawlAsync(GroupSnapshot snapshot);

    // Tenant consent health
    Task UpdateTenantPermissionStatusAsync(string tenantId, IEnumerable<string> missingPermissions);

    // Copilot Chat (unlicensed) usage — Office 365 Management Activity API
    Task<List<CopilotChatUsageSnapshot>> GetCopilotChatUsageAsync(IEnumerable<string>? tenantIds, int days = 30);
    Task SaveCopilotChatUsageAsync(IEnumerable<CopilotChatUsageSnapshot> snapshots);
    Task<DateTime?> GetCopilotAuditCursorAsync(string tenantId);
    Task UpdateCopilotAuditCursorAsync(string tenantId, DateTime cursorUtc);

    // App registration / service-principal credential expiry
    Task<List<AppCredentialSnapshot>> GetAppCredentialsAsync(IEnumerable<string>? tenantIds);
    Task SaveAppCredentialsAsync(string tenantId, DateTime reportDate, IEnumerable<AppCredentialSnapshot> snapshots);

    // External sharing audit — Office 365 Management Activity API (Audit.SharePoint)
    Task<List<ExternalSharingSnapshot>> GetExternalSharingAsync(IEnumerable<string>? tenantIds, int days = 30);
    Task SaveExternalSharingAsync(IEnumerable<ExternalSharingSnapshot> snapshots);
    Task<DateTime?> GetSharePointAuditCursorAsync(string tenantId);
    Task UpdateSharePointAuditCursorAsync(string tenantId, DateTime cursorUtc);

    // Privileged role inventory
    Task<List<PrivilegedRoleSnapshot>> GetPrivilegedRolesAsync(IEnumerable<string>? tenantIds);
    Task SavePrivilegedRolesAsync(string tenantId, DateTime reportDate, IEnumerable<PrivilegedRoleSnapshot> snapshots);

    // Defender / M365 security alerts
    Task<List<DefenderAlertSnapshot>> GetDefenderAlertsAsync(IEnumerable<string>? tenantIds);
    Task SaveDefenderAlertsAsync(string tenantId, DateTime reportDate, IEnumerable<DefenderAlertSnapshot> snapshots);
}

/// <summary>
/// Core data-access service for all snapshot entities. The implementation is split across feature-area
/// partial files for maintainability:
/// <list type="bullet">
/// <item><c>MauDataService.Core.cs</c> — MAU, licenses, message center, sign-in summary, Entra tiers</item>
/// <item><c>MauDataService.Adoption.cs</c> — workload activity, Copilot, segmentation, departments, storage, consumption, M365 apps</item>
/// <item><c>MauDataService.Posture.cs</c> — secure score, MFA, inactive accounts, service health</item>
/// <item><c>MauDataService.Identity.cs</c> — device compliance, conditional access, guests, risky users</item>
/// <item><c>MauDataService.Usage.cs</c> — mailbox, Teams devices, site usage, Yammer, groups</item>
/// <item><c>MauDataService.Governance.cs</c> — consent health, Copilot Chat, app credentials, external sharing, privileged roles, Defender alerts</item>
/// </list>
/// </summary>
public partial class MauDataService : IMauDataService
{
    private readonly IDbContextFactory<MauDbContext> _dbFactory;

    public MauDataService(IDbContextFactory<MauDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }
}
