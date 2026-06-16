using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public partial class MauDataService
{
    // Device Compliance (Intune)
    public async Task<List<DeviceComplianceSnapshot>> GetDeviceComplianceAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.DeviceComplianceSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Latest snapshot per tenant
        return await query
            .GroupBy(s => s.TenantId)
            .Select(g => g.OrderByDescending(s => s.ReportDate).First())
            .ToListAsync();
    }

    public async Task SaveDeviceComplianceAsync(DeviceComplianceSnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.DeviceComplianceSnapshots.FirstOrDefaultAsync(s =>
            s.TenantId == snapshot.TenantId &&
            s.ReportDate == snapshot.ReportDate);

        if (existing != null)
        {
            existing.TenantName = snapshot.TenantName;
            existing.TotalDevices = snapshot.TotalDevices;
            existing.CompliantCount = snapshot.CompliantCount;
            existing.NonCompliantCount = snapshot.NonCompliantCount;
            existing.InGracePeriodCount = snapshot.InGracePeriodCount;
            existing.ErrorCount = snapshot.ErrorCount;
            existing.UnknownCount = snapshot.UnknownCount;
            existing.WindowsCount = snapshot.WindowsCount;
            existing.IosCount = snapshot.IosCount;
            existing.AndroidCount = snapshot.AndroidCount;
            existing.MacOsCount = snapshot.MacOsCount;
            existing.OtherOsCount = snapshot.OtherOsCount;
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.DeviceComplianceSnapshots.Add(snapshot);
        }

        await db.SaveChangesAsync();
    }

    // Device patch / OS-version hygiene (delete-then-insert per (TenantId, ReportDate))
    public async Task<List<DevicePatchSnapshot>> GetDevicePatchAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.DevicePatchSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Keep only rows from the latest ReportDate per tenant (reduced in SQL).
        return await query
            .Where(s => s.ReportDate == query.Where(x => x.TenantId == s.TenantId).Max(x => x.ReportDate))
            .OrderByDescending(s => s.DeviceCount)
            .ToListAsync();
    }

    // Full retained history within the window (drives the stale-device trend chart).
    public async Task<List<DevicePatchSnapshot>> GetDevicePatchHistoryAsync(IEnumerable<string>? tenantIds, int days = 90)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow.AddDays(-days).Date;
        var query = db.DevicePatchSnapshots.AsNoTracking().Where(s => s.ReportDate >= cutoff);
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        return await query.OrderBy(s => s.ReportDate).ToListAsync();
    }

    public async Task SaveDevicePatchAsync(string tenantId, DateTime reportDate, IEnumerable<DevicePatchSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        // Replace the day's rows so removed OS versions don't linger.
        var existing = await db.DevicePatchSnapshots
            .Where(s => s.TenantId == tenantId && s.ReportDate == reportDate)
            .ToListAsync();
        if (existing.Count > 0)
            db.DevicePatchSnapshots.RemoveRange(existing);

        db.DevicePatchSnapshots.AddRange(snapshots);
        await db.SaveChangesAsync();
    }

    // Stale registered devices (delete-then-insert per (TenantId, ReportDate))
    public async Task<List<StaleDeviceSnapshot>> GetStaleDevicesAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.StaleDeviceSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Keep only rows from the latest ReportDate per tenant (reduced in SQL).
        return await query
            .Where(s => s.ReportDate == query.Where(x => x.TenantId == s.TenantId).Max(x => x.ReportDate))
            .OrderByDescending(s => s.Stale90Plus)
            .ToListAsync();
    }

    public async Task SaveStaleDevicesAsync(string tenantId, DateTime reportDate, IEnumerable<StaleDeviceSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.StaleDeviceSnapshots
            .Where(s => s.TenantId == tenantId && s.ReportDate == reportDate)
            .ToListAsync();
        if (existing.Count > 0)
            db.StaleDeviceSnapshots.RemoveRange(existing);

        db.StaleDeviceSnapshots.AddRange(snapshots);
        await db.SaveChangesAsync();
    }

    // Conditional Access
    public async Task<List<ConditionalAccessSnapshot>> GetConditionalAccessAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.ConditionalAccessSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Latest snapshot per tenant
        return await query
            .GroupBy(s => s.TenantId)
            .Select(g => g.OrderByDescending(s => s.ReportDate).First())
            .ToListAsync();
    }

    public async Task SaveConditionalAccessAsync(ConditionalAccessSnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.ConditionalAccessSnapshots.FirstOrDefaultAsync(s =>
            s.TenantId == snapshot.TenantId &&
            s.ReportDate == snapshot.ReportDate);

        if (existing != null)
        {
            existing.TenantName = snapshot.TenantName;
            existing.TotalPolicies = snapshot.TotalPolicies;
            existing.EnabledPolicies = snapshot.EnabledPolicies;
            existing.ReportOnlyPolicies = snapshot.ReportOnlyPolicies;
            existing.DisabledPolicies = snapshot.DisabledPolicies;
            existing.BlocksLegacyAuth = snapshot.BlocksLegacyAuth;
            existing.RequiresMfa = snapshot.RequiresMfa;
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.ConditionalAccessSnapshots.Add(snapshot);
        }

        await db.SaveChangesAsync();
    }

    // Guest Users
    public async Task<List<GuestUserSnapshot>> GetGuestUsersAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.GuestUserSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Latest snapshot per tenant
        return await query
            .GroupBy(s => s.TenantId)
            .Select(g => g.OrderByDescending(s => s.ReportDate).First())
            .ToListAsync();
    }

    public async Task SaveGuestUsersAsync(GuestUserSnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.GuestUserSnapshots.FirstOrDefaultAsync(s =>
            s.TenantId == snapshot.TenantId &&
            s.ReportDate == snapshot.ReportDate);

        if (existing != null)
        {
            existing.TenantName = snapshot.TenantName;
            existing.TotalGuests = snapshot.TotalGuests;
            existing.AcceptedGuests = snapshot.AcceptedGuests;
            existing.PendingAcceptanceGuests = snapshot.PendingAcceptanceGuests;
            existing.RecentlyAddedGuests = snapshot.RecentlyAddedGuests;
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.GuestUserSnapshots.Add(snapshot);
        }

        await db.SaveChangesAsync();
    }

    // Risky Users
    public async Task<List<RiskyUserSnapshot>> GetRiskyUsersAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.RiskyUserSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Latest snapshot per tenant
        return await query
            .GroupBy(s => s.TenantId)
            .Select(g => g.OrderByDescending(s => s.ReportDate).First())
            .ToListAsync();
    }

    public async Task SaveRiskyUsersAsync(RiskyUserSnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.RiskyUserSnapshots.FirstOrDefaultAsync(s =>
            s.TenantId == snapshot.TenantId &&
            s.ReportDate == snapshot.ReportDate);

        if (existing != null)
        {
            existing.TenantName = snapshot.TenantName;
            existing.TotalAtRisk = snapshot.TotalAtRisk;
            existing.HighRisk = snapshot.HighRisk;
            existing.MediumRisk = snapshot.MediumRisk;
            existing.LowRisk = snapshot.LowRisk;
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.RiskyUserSnapshots.Add(snapshot);
        }

        await db.SaveChangesAsync();
    }
}
