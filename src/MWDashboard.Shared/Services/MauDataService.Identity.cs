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
