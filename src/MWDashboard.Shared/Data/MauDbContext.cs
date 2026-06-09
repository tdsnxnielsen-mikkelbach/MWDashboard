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
    public DbSet<UserSegmentSnapshot> UserSegmentSnapshots => Set<UserSegmentSnapshot>();
    public DbSet<DepartmentUsageSnapshot> DepartmentUsageSnapshots => Set<DepartmentUsageSnapshot>();
    public DbSet<StorageSnapshot> StorageSnapshots => Set<StorageSnapshot>();
    public DbSet<ConsumptionSnapshot> ConsumptionSnapshots => Set<ConsumptionSnapshot>();
    public DbSet<M365AppUsageSnapshot> M365AppUsageSnapshots => Set<M365AppUsageSnapshot>();

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
    }
}
