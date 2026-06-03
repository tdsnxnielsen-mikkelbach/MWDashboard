using Microsoft.EntityFrameworkCore;
using MWDashboard.Models;

namespace MWDashboard.Data;

public class MauDbContext : DbContext
{
    public MauDbContext(DbContextOptions<MauDbContext> options) : base(options) { }

    public DbSet<MauSnapshot> MauSnapshots => Set<MauSnapshot>();
    public DbSet<TenantInfo> Tenants => Set<TenantInfo>();
    public DbSet<LicenseSnapshot> LicenseSnapshots => Set<LicenseSnapshot>();

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
    }
}
