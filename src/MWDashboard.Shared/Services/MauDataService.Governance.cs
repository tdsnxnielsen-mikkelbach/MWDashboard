using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public partial class MauDataService
{
    // Tenant consent health
    public async Task UpdateTenantPermissionStatusAsync(string tenantId, IEnumerable<string> missingPermissions)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
        if (tenant == null) return;

        tenant.MissingPermissions = string.Join(",", missingPermissions);
        tenant.PermissionsCheckedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    // Copilot Chat (unlicensed) usage — Office 365 Management Activity API
    public async Task<List<CopilotChatUsageSnapshot>> GetCopilotChatUsageAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var query = db.CopilotChatUsageSnapshots.AsNoTracking().Where(s => s.ReportDate >= cutoff);
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        return await query.OrderBy(s => s.ReportDate).ToListAsync();
    }

    public async Task SaveCopilotChatUsageAsync(IEnumerable<CopilotChatUsageSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var snapshot in snapshots)
        {
            var existing = await db.CopilotChatUsageSnapshots
                .FirstOrDefaultAsync(s =>
                    s.TenantId == snapshot.TenantId &&
                    s.AppHost == snapshot.AppHost &&
                    s.ReportDate == snapshot.ReportDate);

            if (existing != null)
            {
                existing.TenantName = snapshot.TenantName;
                existing.ActiveUsers = snapshot.ActiveUsers;
                existing.InteractionCount = snapshot.InteractionCount;
                existing.UnlicensedUsers = snapshot.UnlicensedUsers;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.CopilotChatUsageSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<DateTime?> GetCopilotAuditCursorAsync(string tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId);
        return tenant?.CopilotAuditCursorUtc;
    }

    public async Task UpdateCopilotAuditCursorAsync(string tenantId, DateTime cursorUtc)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
        if (tenant == null) return;

        // Never move the cursor backwards.
        if (tenant.CopilotAuditCursorUtc == null || cursorUtc > tenant.CopilotAuditCursorUtc)
        {
            tenant.CopilotAuditCursorUtc = cursorUtc;
            await db.SaveChangesAsync();
        }
    }

    // App registration / service-principal credential expiry
    public async Task<List<AppCredentialSnapshot>> GetAppCredentialsAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.AppCredentialSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Keep only rows from the latest ReportDate per tenant (reduced in SQL).
        return await query
            .Where(s => s.ReportDate == query.Where(x => x.TenantId == s.TenantId).Max(x => x.ReportDate))
            .OrderBy(s => s.DaysToExpiry)
            .ToListAsync();
    }

    public async Task SaveAppCredentialsAsync(string tenantId, DateTime reportDate, IEnumerable<AppCredentialSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        // Replace the day's rows so removed credentials don't linger.
        var existing = await db.AppCredentialSnapshots
            .Where(s => s.TenantId == tenantId && s.ReportDate == reportDate)
            .ToListAsync();
        if (existing.Count > 0)
            db.AppCredentialSnapshots.RemoveRange(existing);

        db.AppCredentialSnapshots.AddRange(snapshots);
        await db.SaveChangesAsync();
    }

    // External sharing audit — Office 365 Management Activity API (Audit.SharePoint)
    public async Task<List<ExternalSharingSnapshot>> GetExternalSharingAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var query = db.ExternalSharingSnapshots.AsNoTracking().Where(s => s.ReportDate >= cutoff);
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        return await query.OrderBy(s => s.ReportDate).ToListAsync();
    }

    public async Task SaveExternalSharingAsync(IEnumerable<ExternalSharingSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        foreach (var snapshot in snapshots)
        {
            var existing = await db.ExternalSharingSnapshots.FirstOrDefaultAsync(s =>
                s.TenantId == snapshot.TenantId &&
                s.ShareType == snapshot.ShareType &&
                s.ReportDate == snapshot.ReportDate);
            if (existing != null)
            {
                existing.TenantName = snapshot.TenantName;
                existing.EventCount = snapshot.EventCount;
                existing.DistinctUsers = snapshot.DistinctUsers;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.ExternalSharingSnapshots.Add(snapshot);
            }
        }
        await db.SaveChangesAsync();
    }

    public async Task<DateTime?> GetSharePointAuditCursorAsync(string tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId);
        return tenant?.SharePointAuditCursorUtc;
    }

    public async Task UpdateSharePointAuditCursorAsync(string tenantId, DateTime cursorUtc)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
        if (tenant == null) return;

        // Never move the cursor backwards.
        if (tenant.SharePointAuditCursorUtc == null || cursorUtc > tenant.SharePointAuditCursorUtc)
        {
            tenant.SharePointAuditCursorUtc = cursorUtc;
            await db.SaveChangesAsync();
        }
    }

    // Privileged role inventory
    public async Task<List<PrivilegedRoleSnapshot>> GetPrivilegedRolesAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.PrivilegedRoleSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Keep only rows from the latest ReportDate per tenant (reduced in SQL).
        return await query
            .Where(s => s.ReportDate == query.Where(x => x.TenantId == s.TenantId).Max(x => x.ReportDate))
            .OrderByDescending(s => s.MemberCount)
            .ToListAsync();
    }

    public async Task SavePrivilegedRolesAsync(string tenantId, DateTime reportDate, IEnumerable<PrivilegedRoleSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.PrivilegedRoleSnapshots
            .Where(s => s.TenantId == tenantId && s.ReportDate == reportDate)
            .ToListAsync();
        if (existing.Count > 0)
            db.PrivilegedRoleSnapshots.RemoveRange(existing);

        db.PrivilegedRoleSnapshots.AddRange(snapshots);
        await db.SaveChangesAsync();
    }

    // Defender / M365 security alerts
    public async Task<List<DefenderAlertSnapshot>> GetDefenderAlertsAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.DefenderAlertSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Keep only rows from the latest ReportDate per tenant (reduced in SQL).
        return await query
            .Where(s => s.ReportDate == query.Where(x => x.TenantId == s.TenantId).Max(x => x.ReportDate))
            .ToListAsync();
    }

    public async Task SaveDefenderAlertsAsync(string tenantId, DateTime reportDate, IEnumerable<DefenderAlertSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.DefenderAlertSnapshots
            .Where(s => s.TenantId == tenantId && s.ReportDate == reportDate)
            .ToListAsync();
        if (existing.Count > 0)
            db.DefenderAlertSnapshots.RemoveRange(existing);

        db.DefenderAlertSnapshots.AddRange(snapshots);
        await db.SaveChangesAsync();
    }
}
