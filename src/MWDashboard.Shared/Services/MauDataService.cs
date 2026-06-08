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

        return await query.OrderBy(s => s.ReportDate).ThenBy(s => s.ServiceName).ToListAsync();
    }

    public async Task<List<MauSnapshot>> GetMauHistoryAsync(IEnumerable<string>? tenantIds, int months = 12)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddMonths(-months);
        var query = db.MauSnapshots.Where(s => s.ReportDate >= fromDate);
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

        var query = db.LicenseSnapshots.AsQueryable();
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
        var query = db.LicenseSnapshots.AsQueryable();
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

        var query = db.LicenseSnapshots.Where(l => l.CollectedAt >= from && l.CollectedAt <= to);
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(l => l.TenantId == tenantId);

        return await query.OrderBy(l => l.CollectedAt).ToListAsync();
    }

    public async Task<List<LicenseSnapshot>> GetLicensesByDateRangeAsync(DateTime from, DateTime to, IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.LicenseSnapshots.Where(l => l.CollectedAt >= from && l.CollectedAt <= to);
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

        var query = db.MessageCenterPosts.AsQueryable();
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(p => p.TenantId == tenantId);

        return await query.OrderByDescending(p => p.StartDateTime).Take(100).ToListAsync();
    }

    public async Task<List<MessageCenterPost>> GetMessageCenterPostsAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.MessageCenterPosts.AsQueryable();
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

        var query = db.SecuritySignInSummaries.Where(s => s.ReportDate >= fromDate);
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(s => s.TenantId == tenantId);

        return await query.OrderBy(s => s.ReportDate).ThenBy(s => s.ServiceName).ToListAsync();
    }

    public async Task<List<SecuritySignInSummary>> GetSecuritySummaryAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddDays(-days);
        var query = db.SecuritySignInSummaries.Where(s => s.ReportDate >= fromDate);
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

        var tenants = await db.Tenants.Where(t => t.IsActive).ToListAsync();
        var latestLicenses = await db.LicenseSnapshots
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

    // Workload Activity
    public async Task<List<WorkloadActivitySnapshot>> GetWorkloadActivityAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddDays(-days);
        var query = db.WorkloadActivities.Where(a => a.ReportDate >= fromDate);
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(a => ids.Contains(a.TenantId));
        }
        return await query.OrderBy(a => a.ReportDate).ThenBy(a => a.Workload).ToListAsync();
    }

    public async Task SaveWorkloadActivityAsync(IEnumerable<WorkloadActivitySnapshot> activities)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var activity in activities)
        {
            var existing = await db.WorkloadActivities
                .FirstOrDefaultAsync(a =>
                    a.TenantId == activity.TenantId &&
                    a.Workload == activity.Workload &&
                    a.ActivityType == activity.ActivityType &&
                    a.ReportDate == activity.ReportDate);

            if (existing != null)
            {
                existing.Count = activity.Count;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.WorkloadActivities.Add(activity);
            }
        }

        await db.SaveChangesAsync();
    }

    // Copilot Usage
    public async Task<List<CopilotUsageSnapshot>> GetCopilotUsageAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.CopilotUsageSnapshots.AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(c => ids.Contains(c.TenantId));
        }
        // Get latest snapshot per tenant per app
        return await query
            .GroupBy(c => new { c.TenantId, c.AppName })
            .Select(g => g.OrderByDescending(c => c.ReportDate).First())
            .ToListAsync();
    }

    public async Task SaveCopilotUsageAsync(IEnumerable<CopilotUsageSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var snapshot in snapshots)
        {
            var existing = await db.CopilotUsageSnapshots
                .FirstOrDefaultAsync(c =>
                    c.TenantId == snapshot.TenantId &&
                    c.AppName == snapshot.AppName &&
                    c.ReportDate == snapshot.ReportDate);

            if (existing != null)
            {
                existing.ActiveUsers = snapshot.ActiveUsers;
                existing.TotalAssignedLicenses = snapshot.TotalAssignedLicenses;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.CopilotUsageSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }

    // User Segmentation
    public async Task<List<UserSegmentSnapshot>> GetUserSegmentsAsync(IEnumerable<string>? tenantIds, int months = 6)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddMonths(-months);
        var query = db.UserSegmentSnapshots.Where(s => s.ReportDate >= fromDate);
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        return await query.OrderBy(s => s.ReportDate).ToListAsync();
    }

    public async Task SaveUserSegmentsAsync(IEnumerable<UserSegmentSnapshot> segments)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var segment in segments)
        {
            var existing = await db.UserSegmentSnapshots
                .FirstOrDefaultAsync(s =>
                    s.TenantId == segment.TenantId &&
                    s.ReportDate == segment.ReportDate);

            if (existing != null)
            {
                existing.HeavyUsers = segment.HeavyUsers;
                existing.LightUsers = segment.LightUsers;
                existing.InactiveUsers = segment.InactiveUsers;
                existing.TotalUsers = segment.TotalUsers;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.UserSegmentSnapshots.Add(segment);
            }
        }

        await db.SaveChangesAsync();
    }

    // Department Usage
    public async Task<List<DepartmentUsageSnapshot>> GetDepartmentUsageAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.DepartmentUsageSnapshots.AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(d => ids.Contains(d.TenantId));
        }
        // Latest snapshot per tenant/department
        return await query
            .GroupBy(d => new { d.TenantId, d.Department })
            .Select(g => g.OrderByDescending(d => d.ReportDate).First())
            .ToListAsync();
    }

    public async Task SaveDepartmentUsageAsync(IEnumerable<DepartmentUsageSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var snapshot in snapshots)
        {
            var existing = await db.DepartmentUsageSnapshots
                .FirstOrDefaultAsync(d =>
                    d.TenantId == snapshot.TenantId &&
                    d.Department == snapshot.Department &&
                    d.ReportDate == snapshot.ReportDate);

            if (existing != null)
            {
                existing.ActiveUsers = snapshot.ActiveUsers;
                existing.TotalUsers = snapshot.TotalUsers;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.DepartmentUsageSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }

    // Storage Usage
    public async Task<List<StorageSnapshot>> GetStorageAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddDays(-days);
        var query = db.StorageSnapshots.Where(s => s.ReportDate >= fromDate);
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        return await query.OrderBy(s => s.ReportDate).ToListAsync();
    }

    public async Task SaveStorageAsync(IEnumerable<StorageSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Deduplicate input — keep the last entry per composite key
        var deduped = snapshots
            .GroupBy(s => new { s.TenantId, s.ServiceName, ReportDate = s.ReportDate.Date })
            .Select(g => g.Last())
            .ToList();

        foreach (var snapshot in deduped)
        {
            // Normalize to date-only to avoid time component mismatches
            snapshot.ReportDate = snapshot.ReportDate.Date;

            var existing = await db.StorageSnapshots
                .FirstOrDefaultAsync(s =>
                    s.TenantId == snapshot.TenantId &&
                    s.ServiceName == snapshot.ServiceName &&
                    s.ReportDate == snapshot.ReportDate);

            if (existing != null)
            {
                existing.UsedBytes = snapshot.UsedBytes;
                existing.AllocatedBytes = snapshot.AllocatedBytes;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                snapshot.CollectedAt = DateTime.UtcNow;
                db.StorageSnapshots.Add(snapshot);
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Handle race condition: row was inserted between our check and save
            // Detach failed entries and retry as updates
            foreach (var entry in db.ChangeTracker.Entries<StorageSnapshot>()
                .Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }
            // Reload and update
            foreach (var snapshot in deduped)
            {
                var existing = await db.StorageSnapshots
                    .FirstOrDefaultAsync(s =>
                        s.TenantId == snapshot.TenantId &&
                        s.ServiceName == snapshot.ServiceName &&
                        s.ReportDate == snapshot.ReportDate);
                if (existing != null)
                {
                    existing.UsedBytes = snapshot.UsedBytes;
                    existing.AllocatedBytes = snapshot.AllocatedBytes;
                    existing.CollectedAt = DateTime.UtcNow;
                }
            }
            await db.SaveChangesAsync();
        }
    }

    // Consumption Scores
    public async Task<List<ConsumptionSnapshot>> GetConsumptionAsync(IEnumerable<string>? tenantIds, int months = 6)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddMonths(-months);
        var query = db.ConsumptionSnapshots.Where(c => c.ReportDate >= fromDate);
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(c => ids.Contains(c.TenantId));
        }
        return await query.OrderBy(c => c.ReportDate).ThenBy(c => c.TenantId).ToListAsync();
    }

    public async Task SaveConsumptionAsync(ConsumptionSnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.ConsumptionSnapshots
            .FirstOrDefaultAsync(c =>
                c.TenantId == snapshot.TenantId &&
                c.ReportDate == snapshot.ReportDate);

        if (existing != null)
        {
            existing.StorageUsedBytes = snapshot.StorageUsedBytes;
            existing.StorageAllocatedBytes = snapshot.StorageAllocatedBytes;
            existing.TotalActivityCount = snapshot.TotalActivityCount;
            existing.ActiveUserCount = snapshot.ActiveUserCount;
            existing.LicensedUserCount = snapshot.LicensedUserCount;
            existing.AvgWorkloadsPerUser = snapshot.AvgWorkloadsPerUser;
            existing.ConsumptionScore = snapshot.ConsumptionScore;
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.ConsumptionSnapshots.Add(snapshot);
        }

        await db.SaveChangesAsync();
    }

    // M365 App Platform Usage
    public async Task<List<M365AppUsageSnapshot>> GetM365AppUsageAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.M365AppUsageSnapshots.AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Latest per tenant/app/platform
        return await query
            .GroupBy(s => new { s.TenantId, s.AppName, s.Platform })
            .Select(g => g.OrderByDescending(s => s.ReportDate).First())
            .ToListAsync();
    }

    public async Task SaveM365AppUsageAsync(IEnumerable<M365AppUsageSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var snapshot in snapshots)
        {
            var existing = await db.M365AppUsageSnapshots
                .FirstOrDefaultAsync(s =>
                    s.TenantId == snapshot.TenantId &&
                    s.AppName == snapshot.AppName &&
                    s.Platform == snapshot.Platform &&
                    s.ReportDate == snapshot.ReportDate);

            if (existing != null)
            {
                existing.UserCount = snapshot.UserCount;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.M365AppUsageSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }
}
