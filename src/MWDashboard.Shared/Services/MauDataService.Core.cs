using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public partial class MauDataService
{
    public async Task<List<MauSnapshot>> GetMauHistoryAsync(string? tenantId = null, int months = 12)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddMonths(-months);

        var query = db.MauSnapshots.AsNoTracking().Where(s => s.ReportDate >= fromDate);
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(s => s.TenantId == tenantId);

        return await query.OrderBy(s => s.ReportDate).ThenBy(s => s.ServiceName).ToListAsync();
    }

    public async Task<List<MauSnapshot>> GetMauHistoryAsync(IEnumerable<string>? tenantIds, int months = 12)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddMonths(-months);
        var query = db.MauSnapshots.AsNoTracking().Where(s => s.ReportDate >= fromDate);
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        return await query.OrderBy(s => s.ReportDate).ThenBy(s => s.ServiceName).ToListAsync();
    }

    public async Task<List<MauSnapshot>> GetLatestMauByServiceAsync(string? tenantId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.MauSnapshots.AsQueryable();
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(s => s.TenantId == tenantId);

        // Get the max ReportDate per TenantId+ServiceName, then fetch those rows
        var latestDates = query
            .GroupBy(s => new { s.TenantId, s.ServiceName })
            .Select(g => new { g.Key.TenantId, g.Key.ServiceName, MaxDate = g.Max(s => s.ReportDate) });

        return await query
            .Where(s => latestDates.Any(d =>
                d.TenantId == s.TenantId &&
                d.ServiceName == s.ServiceName &&
                d.MaxDate == s.ReportDate))
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<MauSnapshot>> GetLatestMauByServiceAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.MauSnapshots.AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }

        // Get the max ReportDate per TenantId+ServiceName, then fetch those rows
        var latestDates = query
            .GroupBy(s => new { s.TenantId, s.ServiceName })
            .Select(g => new { g.Key.TenantId, g.Key.ServiceName, MaxDate = g.Max(s => s.ReportDate) });

        return await query
            .Where(s => latestDates.Any(d =>
                d.TenantId == s.TenantId &&
                d.ServiceName == s.ServiceName &&
                d.MaxDate == s.ReportDate))
            .AsNoTracking()
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

        var query = db.LicenseSnapshots.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(s => s.TenantId == tenantId);

        return await query
            .GroupBy(s => new { s.TenantId, s.SkuId })
            .Select(g => g.OrderByDescending(s => s.CollectedAt).First())
            .ToListAsync();
    }

    public async Task<List<LicenseSnapshot>> GetLatestLicensesAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.LicenseSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
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

    public async Task<List<LicenseSnapshot>> GetLicensesByDateRangeAsync(DateTime from, DateTime to, string? tenantId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.LicenseSnapshots.AsNoTracking().Where(l => l.CollectedAt >= from && l.CollectedAt <= to);
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(l => l.TenantId == tenantId);

        return await query.OrderBy(l => l.CollectedAt).ToListAsync();
    }

    public async Task<List<LicenseSnapshot>> GetLicensesByDateRangeAsync(DateTime from, DateTime to, IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.LicenseSnapshots.AsNoTracking().Where(l => l.CollectedAt >= from && l.CollectedAt <= to);
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(l => ids.Contains(l.TenantId));
        }
        return await query.OrderBy(l => l.CollectedAt).ToListAsync();
    }

    public async Task<(DateTime? Earliest, DateTime? Latest)> GetLicenseDataRangeAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        if (await db.MauSnapshots.AnyAsync())
        {
            var earliest = await db.MauSnapshots.MinAsync(s => s.ReportDate);
            var latest = await db.MauSnapshots.MaxAsync(s => s.ReportDate);
            return (earliest, latest);
        }

        if (await db.LicenseSnapshots.AnyAsync())
        {
            var earliest = await db.LicenseSnapshots.MinAsync(l => l.CollectedAt);
            var latest = await db.LicenseSnapshots.MaxAsync(l => l.CollectedAt);
            return (earliest, latest);
        }

        return (null, null);
    }

    public async Task<List<MessageCenterPost>> GetMessageCenterPostsAsync(string? tenantId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.MessageCenterPosts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(p => p.TenantId == tenantId);

        return await query.OrderByDescending(p => p.StartDateTime).Take(100).ToListAsync();
    }

    public async Task<List<MessageCenterPost>> GetMessageCenterPostsAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.MessageCenterPosts.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(p => ids.Contains(p.TenantId));
        }
        return await query.OrderByDescending(p => p.StartDateTime).Take(100).ToListAsync();
    }

    public async Task SaveMessageCenterPostsAsync(IEnumerable<MessageCenterPost> posts)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var post in posts)
        {
            var existing = await db.MessageCenterPosts
                .FirstOrDefaultAsync(p => p.TenantId == post.TenantId && p.MessageId == post.MessageId);

            if (existing != null)
            {
                existing.Title = post.Title;
                existing.Description = post.Description;
                existing.Category = post.Category;
                existing.Severity = post.Severity;
                existing.EndDateTime = post.EndDateTime;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.MessageCenterPosts.Add(post);
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<SecuritySignInSummary>> GetSecuritySummaryAsync(string? tenantId = null, int days = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddDays(-days);

        var query = db.SecuritySignInSummaries.AsNoTracking().Where(s => s.ReportDate >= fromDate);
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(s => s.TenantId == tenantId);

        return await query.OrderBy(s => s.ReportDate).ThenBy(s => s.ServiceName).ToListAsync();
    }

    public async Task<List<SecuritySignInSummary>> GetSecuritySummaryAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddDays(-days);
        var query = db.SecuritySignInSummaries.AsNoTracking().Where(s => s.ReportDate >= fromDate);
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        return await query.OrderBy(s => s.ReportDate).ThenBy(s => s.ServiceName).ToListAsync();
    }

    public async Task SaveSecuritySummariesAsync(IEnumerable<SecuritySignInSummary> summaries)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var summary in summaries)
        {
            var existing = await db.SecuritySignInSummaries
                .FirstOrDefaultAsync(s =>
                    s.TenantId == summary.TenantId &&
                    s.ServiceName == summary.ServiceName &&
                    s.ReportDate == summary.ReportDate);

            if (existing != null)
            {
                existing.ActiveUserCount = summary.ActiveUserCount;
                existing.SuccessCount = summary.SuccessCount;
                existing.FailureCount = summary.FailureCount;
                existing.MfaCount = summary.MfaCount;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.SecuritySignInSummaries.Add(summary);
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<TenantEntraTier>> GetTenantEntraIdTiersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var tenants = await db.Tenants.AsNoTracking().Where(t => t.IsActive).ToListAsync();
        var latestLicenses = await db.LicenseSnapshots
            .AsNoTracking()
            .GroupBy(l => new { l.TenantId, l.SkuId })
            .Select(g => g.OrderByDescending(l => l.CollectedAt).First())
            .ToListAsync();

        var tiers = new List<TenantEntraTier>();
        foreach (var tenant in tenants)
        {
            var skus = latestLicenses
                .Where(l => l.TenantId == tenant.TenantId)
                .Select(l => l.SkuPartNumber);

            tiers.Add(TenantEntraTier.FromLicenses(tenant.TenantId, tenant.DisplayName, skus));
        }

        return tiers;
    }
}
