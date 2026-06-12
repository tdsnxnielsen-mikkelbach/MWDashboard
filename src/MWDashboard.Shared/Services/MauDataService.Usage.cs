using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public partial class MauDataService
{
    // Mailbox Usage (Tier 3)
    public async Task<List<MailboxUsageSnapshot>> GetMailboxUsageAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.MailboxUsageSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        var all = await query.ToListAsync();
        // Latest snapshot per tenant
        return all.LatestPerKey(s => s.TenantId, s => s.ReportDate);
    }

    public async Task SaveMailboxUsageAsync(MailboxUsageSnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.MailboxUsageSnapshots.FirstOrDefaultAsync(s =>
            s.TenantId == snapshot.TenantId && s.ReportDate == snapshot.ReportDate);
        if (existing != null)
        {
            existing.TenantName = snapshot.TenantName;
            existing.TotalMailboxes = snapshot.TotalMailboxes;
            existing.ActiveMailboxes = snapshot.ActiveMailboxes;
            existing.InactiveMailboxes = snapshot.InactiveMailboxes;
            existing.TotalStorageUsedBytes = snapshot.TotalStorageUsedBytes;
            existing.UnderLimitCount = snapshot.UnderLimitCount;
            existing.WarningCount = snapshot.WarningCount;
            existing.SendProhibitedCount = snapshot.SendProhibitedCount;
            existing.SendReceiveProhibitedCount = snapshot.SendReceiveProhibitedCount;
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.MailboxUsageSnapshots.Add(snapshot);
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<TopMailboxSnapshot>> GetTopMailboxesAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.TopMailboxSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        var all = await query.ToListAsync();
        // Only the latest collection per tenant
        return all
            .LatestDateRowsPerKey(s => s.TenantId, s => s.ReportDate)
            .OrderBy(s => s.TenantId).ThenBy(s => s.Rank)
            .ToList();
    }

    public async Task SaveTopMailboxesAsync(IEnumerable<TopMailboxSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        foreach (var snapshot in snapshots)
        {
            var existing = await db.TopMailboxSnapshots.FirstOrDefaultAsync(s =>
                s.TenantId == snapshot.TenantId && s.ReportDate == snapshot.ReportDate && s.Rank == snapshot.Rank);
            if (existing != null)
            {
                existing.TenantName = snapshot.TenantName;
                existing.DisplayName = snapshot.DisplayName;
                existing.StorageUsedBytes = snapshot.StorageUsedBytes;
                existing.ItemCount = snapshot.ItemCount;
                existing.LastActivityDate = snapshot.LastActivityDate;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.TopMailboxSnapshots.Add(snapshot);
            }
        }
        await db.SaveChangesAsync();
    }

    // Teams Device Usage (Tier 3)
    public async Task<List<TeamsDeviceUsageSnapshot>> GetTeamsDeviceUsageAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.TeamsDeviceUsageSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        var all = await query.ToListAsync();
        // Latest snapshot per tenant
        return all.LatestPerKey(s => s.TenantId, s => s.ReportDate);
    }

    public async Task SaveTeamsDeviceUsageAsync(TeamsDeviceUsageSnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.TeamsDeviceUsageSnapshots.FirstOrDefaultAsync(s =>
            s.TenantId == snapshot.TenantId && s.ReportDate == snapshot.ReportDate);
        if (existing != null)
        {
            existing.TenantName = snapshot.TenantName;
            existing.WindowsCount = snapshot.WindowsCount;
            existing.MacCount = snapshot.MacCount;
            existing.WebCount = snapshot.WebCount;
            existing.IosCount = snapshot.IosCount;
            existing.AndroidPhoneCount = snapshot.AndroidPhoneCount;
            existing.WindowsPhoneCount = snapshot.WindowsPhoneCount;
            existing.ChromeOsCount = snapshot.ChromeOsCount;
            existing.LinuxCount = snapshot.LinuxCount;
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.TeamsDeviceUsageSnapshots.Add(snapshot);
        }
        await db.SaveChangesAsync();
    }

    // SharePoint / OneDrive Site Usage (Tier 3)
    public async Task<List<SiteUsageSnapshot>> GetSiteUsageAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.SiteUsageSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        var all = await query.ToListAsync();
        // Latest per (tenant, workload)
        return all.LatestPerKey(s => new { s.TenantId, s.Workload }, s => s.ReportDate);
    }

    public async Task SaveSiteUsageAsync(IEnumerable<SiteUsageSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        foreach (var snapshot in snapshots)
        {
            var existing = await db.SiteUsageSnapshots.FirstOrDefaultAsync(s =>
                s.TenantId == snapshot.TenantId && s.Workload == snapshot.Workload && s.ReportDate == snapshot.ReportDate);
            if (existing != null)
            {
                existing.TenantName = snapshot.TenantName;
                existing.TotalSites = snapshot.TotalSites;
                existing.ActiveSites = snapshot.ActiveSites;
                existing.TotalStorageUsedBytes = snapshot.TotalStorageUsedBytes;
                existing.TotalFileCount = snapshot.TotalFileCount;
                existing.ActiveFileCount = snapshot.ActiveFileCount;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.SiteUsageSnapshots.Add(snapshot);
            }
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<SiteUsageDetailSnapshot>> GetSiteUsageDetailAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.SiteUsageDetailSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        var all = await query.ToListAsync();
        // Latest collection per (tenant, workload)
        return all
            .LatestDateRowsPerKey(s => new { s.TenantId, s.Workload }, s => s.ReportDate)
            .OrderBy(s => s.TenantId).ThenBy(s => s.Workload).ThenBy(s => s.Rank)
            .ToList();
    }

    public async Task SaveSiteUsageDetailAsync(IEnumerable<SiteUsageDetailSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        foreach (var snapshot in snapshots)
        {
            var existing = await db.SiteUsageDetailSnapshots.FirstOrDefaultAsync(s =>
                s.TenantId == snapshot.TenantId && s.Workload == snapshot.Workload &&
                s.ReportDate == snapshot.ReportDate && s.Rank == snapshot.Rank);
            if (existing != null)
            {
                existing.TenantName = snapshot.TenantName;
                existing.Name = snapshot.Name;
                existing.StorageUsedBytes = snapshot.StorageUsedBytes;
                existing.FileCount = snapshot.FileCount;
                existing.ActiveFileCount = snapshot.ActiveFileCount;
                existing.LastActivityDate = snapshot.LastActivityDate;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.SiteUsageDetailSnapshots.Add(snapshot);
            }
        }
        await db.SaveChangesAsync();
    }

    // Viva Engage / Yammer (Tier 3)
    public async Task<List<YammerActivitySnapshot>> GetYammerActivityAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.YammerActivitySnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        var all = await query.ToListAsync();
        // Latest snapshot per tenant
        return all.LatestPerKey(s => s.TenantId, s => s.ReportDate);
    }

    public async Task SaveYammerActivityAsync(YammerActivitySnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.YammerActivitySnapshots.FirstOrDefaultAsync(s =>
            s.TenantId == snapshot.TenantId && s.ReportDate == snapshot.ReportDate);
        if (existing != null)
        {
            existing.TenantName = snapshot.TenantName;
            existing.PostedCount = snapshot.PostedCount;
            existing.ReadCount = snapshot.ReadCount;
            existing.LikedCount = snapshot.LikedCount;
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.YammerActivitySnapshots.Add(snapshot);
        }
        await db.SaveChangesAsync();
    }

    // Groups & Teams sprawl (Tier 3)
    public async Task<List<GroupSnapshot>> GetGroupSprawlAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.GroupSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        var all = await query.ToListAsync();
        // Latest snapshot per tenant
        return all.LatestPerKey(s => s.TenantId, s => s.ReportDate);
    }

    public async Task SaveGroupSprawlAsync(GroupSnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.GroupSnapshots.FirstOrDefaultAsync(s =>
            s.TenantId == snapshot.TenantId && s.ReportDate == snapshot.ReportDate);
        if (existing != null)
        {
            existing.TenantName = snapshot.TenantName;
            existing.TotalGroups = snapshot.TotalGroups;
            existing.M365Groups = snapshot.M365Groups;
            existing.SecurityGroups = snapshot.SecurityGroups;
            existing.DistributionGroups = snapshot.DistributionGroups;
            existing.TeamsConnectedGroups = snapshot.TeamsConnectedGroups;
            existing.OwnerlessGroups = snapshot.OwnerlessGroups;
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.GroupSnapshots.Add(snapshot);
        }
        await db.SaveChangesAsync();
    }
}
