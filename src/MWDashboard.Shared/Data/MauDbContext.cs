using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Data;

public class MauDbContext : DbContext
{
    public MauDbContext(DbContextOptions<MauDbContext> options) : base(options) { }

    public DbSet<MauSnapshot> MauSnapshots => Set<MauSnapshot>();
    public DbSet<TenantInfo> Tenants => Set<TenantInfo>();
    public DbSet<LicenseSnapshot> LicenseSnapshots => Set<LicenseSnapshot>();
    public DbSet<MessageCenterPost> MessageCenterPosts => Set<MessageCenterPost>();
    public DbSet<SecuritySignInSummary> SecuritySignInSummaries => Set<SecuritySignInSummary>();
    public DbSet<WorkloadActivitySnapshot> WorkloadActivities => Set<WorkloadActivitySnapshot>();
    public DbSet<CopilotUsageSnapshot> CopilotUsageSnapshots => Set<CopilotUsageSnapshot>();
    public DbSet<UserSegmentSnapshot> UserSegmentSnapshots => Set<UserSegmentSnapshot>();    public DbSet<DepartmentUsageSnapshot> DepartmentUsageSnapshots => Set<DepartmentUsageSnapshot>();
    public DbSet<StorageSnapshot> StorageSnapshots => Set<StorageSnapshot>();
    public DbSet<ConsumptionSnapshot> ConsumptionSnapshots => Set<ConsumptionSnapshot>();
    public DbSet<M365AppUsageSnapshot> M365AppUsageSnapshots => Set<M365AppUsageSnapshot>();
    public DbSet<M365AppUserDetailSnapshot> M365AppUserDetailSnapshots => Set<M365AppUserDetailSnapshot>();
    public DbSet<Office365ActivationSnapshot> Office365ActivationSnapshots => Set<Office365ActivationSnapshot>();
    public DbSet<Office365ActivationUserSnapshot> Office365ActivationUserSnapshots => Set<Office365ActivationUserSnapshot>();
    public DbSet<SecureScoreSnapshot> SecureScoreSnapshots => Set<SecureScoreSnapshot>();
    public DbSet<SecureScoreControlSnapshot> SecureScoreControlSnapshots => Set<SecureScoreControlSnapshot>();
    public DbSet<MfaRegistrationSnapshot> MfaRegistrationSnapshots => Set<MfaRegistrationSnapshot>();
    public DbSet<InactiveAccountSnapshot> InactiveAccountSnapshots => Set<InactiveAccountSnapshot>();
    public DbSet<ServiceHealthSnapshot> ServiceHealthSnapshots => Set<ServiceHealthSnapshot>();
    public DbSet<ServiceHealthIssueSnapshot> ServiceHealthIssueSnapshots => Set<ServiceHealthIssueSnapshot>();
    public DbSet<DeviceComplianceSnapshot> DeviceComplianceSnapshots => Set<DeviceComplianceSnapshot>();
    public DbSet<DevicePatchSnapshot> DevicePatchSnapshots => Set<DevicePatchSnapshot>();
    public DbSet<ConditionalAccessSnapshot> ConditionalAccessSnapshots => Set<ConditionalAccessSnapshot>();
    public DbSet<GuestUserSnapshot> GuestUserSnapshots => Set<GuestUserSnapshot>();
    public DbSet<RiskyUserSnapshot> RiskyUserSnapshots => Set<RiskyUserSnapshot>();
    public DbSet<MailboxUsageSnapshot> MailboxUsageSnapshots => Set<MailboxUsageSnapshot>();
    public DbSet<TopMailboxSnapshot> TopMailboxSnapshots => Set<TopMailboxSnapshot>();
    public DbSet<TeamsDeviceUsageSnapshot> TeamsDeviceUsageSnapshots => Set<TeamsDeviceUsageSnapshot>();
    public DbSet<SiteUsageSnapshot> SiteUsageSnapshots => Set<SiteUsageSnapshot>();
    public DbSet<SiteUsageDetailSnapshot> SiteUsageDetailSnapshots => Set<SiteUsageDetailSnapshot>();
    public DbSet<YammerActivitySnapshot> YammerActivitySnapshots => Set<YammerActivitySnapshot>();
    public DbSet<GroupSnapshot> GroupSnapshots => Set<GroupSnapshot>();
    public DbSet<BrandingSettings> BrandingSettings => Set<BrandingSettings>();
    public DbSet<CopilotChatUsageSnapshot> CopilotChatUsageSnapshots => Set<CopilotChatUsageSnapshot>();
    public DbSet<AppCredentialSnapshot> AppCredentialSnapshots => Set<AppCredentialSnapshot>();
    public DbSet<ExternalSharingSnapshot> ExternalSharingSnapshots => Set<ExternalSharingSnapshot>();
    public DbSet<PrivilegedRoleSnapshot> PrivilegedRoleSnapshots => Set<PrivilegedRoleSnapshot>();
    public DbSet<DefenderAlertSnapshot> DefenderAlertSnapshots => Set<DefenderAlertSnapshot>();
    public DbSet<MailRuleEventSnapshot> MailRuleEventSnapshots => Set<MailRuleEventSnapshot>();
    public DbSet<DlpEventSnapshot> DlpEventSnapshots => Set<DlpEventSnapshot>();
    public DbSet<SubscriptionSnapshot> SubscriptionSnapshots => Set<SubscriptionSnapshot>();
    public DbSet<TeamsTeamActivitySnapshot> TeamsTeamActivitySnapshots => Set<TeamsTeamActivitySnapshot>();
    public DbSet<DirectoryAuditSnapshot> DirectoryAuditSnapshots => Set<DirectoryAuditSnapshot>();
    public DbSet<LicenseAssignmentIssueSnapshot> LicenseAssignmentIssueSnapshots => Set<LicenseAssignmentIssueSnapshot>();
    public DbSet<OAuthGrantSnapshot> OAuthGrantSnapshots => Set<OAuthGrantSnapshot>();
    public DbSet<MailboxAccessSnapshot> MailboxAccessSnapshots => Set<MailboxAccessSnapshot>();
    public DbSet<SignInDetailSnapshot> SignInDetailSnapshots => Set<SignInDetailSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MauSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ServiceName, e.ReportDate }).IsUnique();
            entity.Property(e => e.ServiceName).HasMaxLength(100);
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<TenantInfo>(entity =>
        {
            entity.HasIndex(e => e.TenantId).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.DisplayName).HasMaxLength(250);
        });

        modelBuilder.Entity<LicenseSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.SkuId, e.CollectedAt });
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.SkuId).HasMaxLength(100);
            entity.Property(e => e.SkuPartNumber).HasMaxLength(250);
            entity.Property(e => e.IncludedServices).HasMaxLength(500);
        });

        modelBuilder.Entity<MessageCenterPost>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.MessageId }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.MessageId).HasMaxLength(100);
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.Severity).HasMaxLength(50);
        });

        modelBuilder.Entity<SecuritySignInSummary>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ServiceName, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.ServiceName).HasMaxLength(100);
        });

        modelBuilder.Entity<WorkloadActivitySnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Workload, e.ActivityType, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.Workload).HasMaxLength(100);
            entity.Property(e => e.ActivityType).HasMaxLength(100);
        });

        modelBuilder.Entity<CopilotUsageSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.AppName, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.AppName).HasMaxLength(100);
        });

        modelBuilder.Entity<UserSegmentSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<DepartmentUsageSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Department, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.Department).HasMaxLength(250);
        });

        modelBuilder.Entity<StorageSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ServiceName, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.ServiceName).HasMaxLength(100);
        });

        modelBuilder.Entity<ConsumptionSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<M365AppUsageSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.AppName, e.Platform, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.AppName).HasMaxLength(100);
            entity.Property(e => e.Platform).HasMaxLength(100);
        });

        modelBuilder.Entity<M365AppUserDetailSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.UserKey, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.UserKey).HasMaxLength(64);
        });

        modelBuilder.Entity<Office365ActivationSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ProductType, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.ProductType).HasMaxLength(200);
        });

        modelBuilder.Entity<Office365ActivationUserSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.UserKey, e.ProductType, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.UserKey).HasMaxLength(64);
            entity.Property(e => e.ProductType).HasMaxLength(200);
        });

        modelBuilder.Entity<SecureScoreSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<SecureScoreControlSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ControlName, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.ControlName).HasMaxLength(250);
            entity.Property(e => e.ControlCategory).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.ImplementationStatus).HasMaxLength(500);
        });

        modelBuilder.Entity<MfaRegistrationSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<InactiveAccountSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<ServiceHealthSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ServiceName, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.ServiceName).HasMaxLength(250);
            entity.Property(e => e.Status).HasMaxLength(100);
        });

        modelBuilder.Entity<ServiceHealthIssueSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.IssueId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.IssueId).HasMaxLength(100);
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.ServiceName).HasMaxLength(250);
            entity.Property(e => e.Classification).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(100);
            entity.Property(e => e.Feature).HasMaxLength(250);
        });

        modelBuilder.Entity<DeviceComplianceSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<DevicePatchSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.OsPlatform, e.OsVersion, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.OsPlatform).HasMaxLength(100);
            entity.Property(e => e.OsVersion).HasMaxLength(100);
        });

        modelBuilder.Entity<ConditionalAccessSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<GuestUserSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<RiskyUserSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<MailboxUsageSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<TopMailboxSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate, e.Rank }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.DisplayName).HasMaxLength(320);
        });

        modelBuilder.Entity<TeamsDeviceUsageSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<SiteUsageSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Workload, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.Workload).HasMaxLength(50);
        });

        modelBuilder.Entity<SiteUsageDetailSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Workload, e.ReportDate, e.Rank }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.Workload).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(450);
        });

        modelBuilder.Entity<YammerActivitySnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<GroupSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
        });

        modelBuilder.Entity<BrandingSettings>(entity =>
        {
            entity.Property(e => e.LightPrimary).HasMaxLength(20);
            entity.Property(e => e.LightSecondary).HasMaxLength(20);
            entity.Property(e => e.LightAppbar).HasMaxLength(20);
            entity.Property(e => e.DarkPrimary).HasMaxLength(20);
            entity.Property(e => e.DarkSecondary).HasMaxLength(20);
            entity.Property(e => e.DarkAppbar).HasMaxLength(20);
            entity.Property(e => e.LogoContentType).HasMaxLength(50);
            entity.Property(e => e.FaviconContentType).HasMaxLength(50);
            entity.Property(e => e.AppTitle).HasMaxLength(100);
        });

        modelBuilder.Entity<CopilotChatUsageSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.AppHost, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.AppHost).HasMaxLength(100);
        });

        modelBuilder.Entity<AppCredentialSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ReportDate, e.AppObjectId, e.KeyId }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.AppId).HasMaxLength(100);
            entity.Property(e => e.AppObjectId).HasMaxLength(100);
            entity.Property(e => e.AppDisplayName).HasMaxLength(250);
            entity.Property(e => e.CredentialType).HasMaxLength(20);
            entity.Property(e => e.KeyId).HasMaxLength(100);
            entity.Property(e => e.DisplayName).HasMaxLength(250);
        });

        modelBuilder.Entity<ExternalSharingSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ShareType, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.ShareType).HasMaxLength(30);
        });

        modelBuilder.Entity<PrivilegedRoleSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.RoleName, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.RoleName).HasMaxLength(200);
            entity.Property(e => e.RoleTemplateId).HasMaxLength(100);
        });

        modelBuilder.Entity<DefenderAlertSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Severity, e.Status, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.Severity).HasMaxLength(30);
            entity.Property(e => e.Status).HasMaxLength(30);
        });

        modelBuilder.Entity<MailRuleEventSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.RuleType, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.RuleType).HasMaxLength(30);
        });

        modelBuilder.Entity<DlpEventSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.PolicyName, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.PolicyName).HasMaxLength(250);
            entity.Property(e => e.Severity).HasMaxLength(30);
        });

        modelBuilder.Entity<SubscriptionSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.SkuId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.SkuId).HasMaxLength(100);
            entity.Property(e => e.SkuPartNumber).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(30);
        });

        modelBuilder.Entity<TeamsTeamActivitySnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.TeamId, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.TeamId).HasMaxLength(100);
            entity.Property(e => e.TeamName).HasMaxLength(250);
            entity.Property(e => e.TeamType).HasMaxLength(30);
        });

        modelBuilder.Entity<DirectoryAuditSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Category, e.Activity, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.Activity).HasMaxLength(250);
        });

        modelBuilder.Entity<LicenseAssignmentIssueSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.SkuPartNumber, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.SkuPartNumber).HasMaxLength(100);
            entity.Property(e => e.SkuId).HasMaxLength(100);
        });

        modelBuilder.Entity<OAuthGrantSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.AppId, e.GrantType, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.AppDisplayName).HasMaxLength(250);
            entity.Property(e => e.AppId).HasMaxLength(100);
            entity.Property(e => e.GrantType).HasMaxLength(20);
            entity.Property(e => e.HighRiskScopes).HasMaxLength(2000);
        });

        modelBuilder.Entity<MailboxAccessSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.AccessType, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.AccessType).HasMaxLength(30);
        });

        modelBuilder.Entity<SignInDetailSnapshot>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ClientApp, e.Country, e.ReportDate }).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.TenantName).HasMaxLength(250);
            entity.Property(e => e.ClientApp).HasMaxLength(100);
            entity.Property(e => e.Country).HasMaxLength(100);
        });
    }
}