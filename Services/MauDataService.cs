using Microsoft.EntityFrameworkCore;
using MWDashboard.Data;
using MWDashboard.Models;

namespace MWDashboard.Services;

public interface IMauDataService
{
    Task<List<MauSnapshot>> GetMauHistoryAsync(string? tenantId = null, int months = 12);
    Task<List<MauSnapshot>> GetLatestMauByServiceAsync(string? tenantId = null);
    Task SaveSnapshotsAsync(IEnumerable<MauSnapshot> snapshots);
    Task<List<LicenseSnapshot>> GetLatestLicensesAsync(string? tenantId = null);
    Task SaveLicensesAsync(IEnumerable<LicenseSnapshot> licenses);
}

public class MauDataService : IMauDataService
{
    private readonly IDbContextFactory<MauDbContext> _dbFactory;

    public MauDataService(IDbContextFactory<MauDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<MauSnapshot>> GetMauHistoryAsync(string? tenantId = null, int months = 12)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddMonths(-months);

        var query = db.MauSnapshots.Where(s => s.ReportDate >= fromDate);
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(s => s.TenantId == tenantId);

        return await query
            .OrderBy(s => s.ReportDate)
            .ThenBy(s => s.ServiceName)
            .ToListAsync();
    }

    public async Task<List<MauSnapshot>> GetLatestMauByServiceAsync(string? tenantId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.MauSnapshots.AsQueryable();
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(s => s.TenantId == tenantId);

        return await query
            .GroupBy(s => s.ServiceName)
            .Select(g => g.OrderByDescending(s => s.ReportDate).First())
            .ToListAsync();
    }

    public async Task SaveSnapshotsAsync(IEnumerable<MauSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var snapshot in snapshots)
        {
            var existing = await db.MauSnapshots
                .FirstOrDefaultAsync(s =>
                    s.TenantId == snapshot.TenantId &&
                    s.ServiceName == snapshot.ServiceName &&
                    s.ReportDate == snapshot.ReportDate);

            if (existing != null)
            {
                existing.ActiveUserCount = snapshot.ActiveUserCount;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.MauSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<LicenseSnapshot>> GetLatestLicensesAsync(string? tenantId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.LicenseSnapshots.AsQueryable();
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(s => s.TenantId == tenantId);

        return await query
            .GroupBy(s => new { s.TenantId, s.SkuId })
            .Select(g => g.OrderByDescending(s => s.CollectedAt).First())
            .ToListAsync();
    }

    public async Task SaveLicensesAsync(IEnumerable<LicenseSnapshot> licenses)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.LicenseSnapshots.AddRange(licenses);
        await db.SaveChangesAsync();
    }
}
