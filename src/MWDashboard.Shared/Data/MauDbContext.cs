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
    }
}
