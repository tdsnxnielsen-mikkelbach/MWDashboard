using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public partial class MauDataService
{
    // Workload Activity
    public async Task<List<WorkloadActivitySnapshot>> GetWorkloadActivityAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fromDate = DateTime.UtcNow.AddDays(-days);
        var query = db.WorkloadActivities.AsNoTracking().Where(a => a.ReportDate >= fromDate);
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
        var query = db.CopilotUsageSnapshots.AsNoTracking().AsQueryable();
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
        var query = db.UserSegmentSnapshots.AsNoTracking().Where(s => s.ReportDate >= fromDate);
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
        var query = db.DepartmentUsageSnapshots.AsNoTracking().AsQueryable();
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
        var query = db.StorageSnapshots.AsNoTracking().Where(s => s.ReportDate >= fromDate);
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
        var query = db.ConsumptionSnapshots.AsNoTracking().Where(c => c.ReportDate >= fromDate);
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
        var query = db.M365AppUsageSnapshots.AsNoTracking().AsQueryable();
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

    // M365 App per-user detail (anonymized) — returns the latest collection per tenant.
    public async Task<List<M365AppUserDetailSnapshot>> GetM365AppUserDetailAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.M365AppUserDetailSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        return await query
            .Where(s => s.ReportDate == query.Where(x => x.TenantId == s.TenantId).Max(x => x.ReportDate))
            .ToListAsync();
    }

    public async Task SaveM365AppUserDetailAsync(IEnumerable<M365AppUserDetailSnapshot> snapshots)
    {
        var list = snapshots as IList<M365AppUserDetailSnapshot> ?? snapshots.ToList();
        if (list.Count == 0) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Batch-upsert by (TenantId, UserKey, ReportDate): load the existing rows for the affected
        // tenant/date once, then update or insert in memory (avoids one query per user).
        var tenantId = list[0].TenantId;
        var reportDate = list[0].ReportDate;
        var existing = await db.M365AppUserDetailSnapshots
            .Where(s => s.TenantId == tenantId && s.ReportDate == reportDate)
            .ToDictionaryAsync(s => s.UserKey);

        foreach (var snapshot in list)
        {
            if (existing.TryGetValue(snapshot.UserKey, out var row))
            {
                snapshot.Id = row.Id;
                db.Entry(row).CurrentValues.SetValues(snapshot);
            }
            else
            {
                db.M365AppUserDetailSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }

    // Office 365 activation counts (aggregate per product/device) — latest collection per tenant.
    public async Task<List<Office365ActivationSnapshot>> GetOffice365ActivationsAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Office365ActivationSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        return await query
            .Where(s => s.ReportDate == query.Where(x => x.TenantId == s.TenantId).Max(x => x.ReportDate))
            .OrderBy(s => s.ProductType)
            .ToListAsync();
    }

    public async Task SaveOffice365ActivationsAsync(IEnumerable<Office365ActivationSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var snapshot in snapshots)
        {
            var existing = await db.Office365ActivationSnapshots
                .FirstOrDefaultAsync(s =>
                    s.TenantId == snapshot.TenantId &&
                    s.ProductType == snapshot.ProductType &&
                    s.ReportDate == snapshot.ReportDate);

            if (existing != null)
            {
                existing.WindowsCount = snapshot.WindowsCount;
                existing.MacCount = snapshot.MacCount;
                existing.AndroidCount = snapshot.AndroidCount;
                existing.IosCount = snapshot.IosCount;
                existing.WindowsMobileCount = snapshot.WindowsMobileCount;
                existing.TenantName = snapshot.TenantName;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.Office365ActivationSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }

    // Office 365 activation per-user detail (anonymized) — latest collection per tenant.
    public async Task<List<Office365ActivationUserSnapshot>> GetOffice365ActivationUsersAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Office365ActivationUserSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        return await query
            .Where(s => s.ReportDate == query.Where(x => x.TenantId == s.TenantId).Max(x => x.ReportDate))
            .ToListAsync();
    }

    public async Task SaveOffice365ActivationUsersAsync(IEnumerable<Office365ActivationUserSnapshot> snapshots)
    {
        var list = snapshots as IList<Office365ActivationUserSnapshot> ?? snapshots.ToList();
        if (list.Count == 0) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var tenantId = list[0].TenantId;
        var reportDate = list[0].ReportDate;
        var existing = await db.Office365ActivationUserSnapshots
            .Where(s => s.TenantId == tenantId && s.ReportDate == reportDate)
            .ToListAsync();
        var existingByKey = existing
            .GroupBy(s => (s.UserKey, s.ProductType))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var snapshot in list)
        {
            if (existingByKey.TryGetValue((snapshot.UserKey, snapshot.ProductType), out var row))
            {
                snapshot.Id = row.Id;
                db.Entry(row).CurrentValues.SetValues(snapshot);
            }
            else
            {
                db.Office365ActivationUserSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }
}
