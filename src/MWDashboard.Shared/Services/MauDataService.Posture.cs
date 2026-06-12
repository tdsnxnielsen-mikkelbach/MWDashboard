using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public partial class MauDataService
{
    // Secure Score
    public async Task<List<SecureScoreSnapshot>> GetSecureScoresAsync(IEnumerable<string>? tenantIds, int days = 90)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var query = db.SecureScoreSnapshots.AsNoTracking().Where(s => s.ReportDate >= cutoff);
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        return await query.OrderBy(s => s.ReportDate).ToListAsync();
    }

    public async Task SaveSecureScoresAsync(IEnumerable<SecureScoreSnapshot> snapshots)
    {
        var list = snapshots.ToList();
        if (list.Count == 0) return;
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var snapshot in list)
        {
            var existing = await db.SecureScoreSnapshots.FirstOrDefaultAsync(s =>
                s.TenantId == snapshot.TenantId &&
                s.ReportDate == snapshot.ReportDate);

            if (existing != null)
            {
                existing.TenantName = snapshot.TenantName;
                existing.CurrentScore = snapshot.CurrentScore;
                existing.MaxScore = snapshot.MaxScore;
                existing.ActiveUserCount = snapshot.ActiveUserCount;
                existing.LicensedUserCount = snapshot.LicensedUserCount;
                existing.ComparativeScoreAllTenants = snapshot.ComparativeScoreAllTenants;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.SecureScoreSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<SecureScoreControlSnapshot>> GetSecureScoreControlsAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.SecureScoreControlSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Latest report per tenant/control
        return await query
            .GroupBy(s => new { s.TenantId, s.ControlName })
            .Select(g => g.OrderByDescending(s => s.ReportDate).First())
            .ToListAsync();
    }

    public async Task SaveSecureScoreControlsAsync(IEnumerable<SecureScoreControlSnapshot> snapshots)
    {
        var list = snapshots.ToList();
        if (list.Count == 0) return;
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var snapshot in list)
        {
            var existing = await db.SecureScoreControlSnapshots.FirstOrDefaultAsync(s =>
                s.TenantId == snapshot.TenantId &&
                s.ControlName == snapshot.ControlName &&
                s.ReportDate == snapshot.ReportDate);

            if (existing != null)
            {
                existing.TenantName = snapshot.TenantName;
                existing.ControlCategory = snapshot.ControlCategory;
                existing.Description = snapshot.Description;
                existing.Score = snapshot.Score;
                existing.ScoreInPercentage = snapshot.ScoreInPercentage;
                existing.ImplementationStatus = snapshot.ImplementationStatus;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.SecureScoreControlSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }

    // MFA Registration
    public async Task<List<MfaRegistrationSnapshot>> GetMfaRegistrationAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.MfaRegistrationSnapshots.AsNoTracking().AsQueryable();
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

    public async Task SaveMfaRegistrationAsync(MfaRegistrationSnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.MfaRegistrationSnapshots.FirstOrDefaultAsync(s =>
            s.TenantId == snapshot.TenantId &&
            s.ReportDate == snapshot.ReportDate);

        if (existing != null)
        {
            existing.TenantName = snapshot.TenantName;
            existing.TotalUsers = snapshot.TotalUsers;
            existing.MfaRegistered = snapshot.MfaRegistered;
            existing.MfaCapable = snapshot.MfaCapable;
            existing.PasswordlessCapable = snapshot.PasswordlessCapable;
            existing.SsprRegistered = snapshot.SsprRegistered;
            existing.SsprCapable = snapshot.SsprCapable;
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.MfaRegistrationSnapshots.Add(snapshot);
        }

        await db.SaveChangesAsync();
    }

    // Inactive Accounts
    public async Task<List<InactiveAccountSnapshot>> GetInactiveAccountsAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.InactiveAccountSnapshots.AsNoTracking().AsQueryable();
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

    public async Task SaveInactiveAccountsAsync(InactiveAccountSnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.InactiveAccountSnapshots.FirstOrDefaultAsync(s =>
            s.TenantId == snapshot.TenantId &&
            s.ReportDate == snapshot.ReportDate);

        if (existing != null)
        {
            existing.TenantName = snapshot.TenantName;
            existing.TotalLicensedUsers = snapshot.TotalLicensedUsers;
            existing.Inactive30 = snapshot.Inactive30;
            existing.Inactive60 = snapshot.Inactive60;
            existing.Inactive90 = snapshot.Inactive90;
            existing.NeverSignedIn = snapshot.NeverSignedIn;
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.InactiveAccountSnapshots.Add(snapshot);
        }

        await db.SaveChangesAsync();
    }

    // Service Health
    public async Task<List<ServiceHealthSnapshot>> GetServiceHealthAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.ServiceHealthSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Latest report date per tenant, then return that date's per-service rows
        var all = await query.ToListAsync();
        return all.LatestDateRowsPerKey(s => s.TenantId, s => s.ReportDate).ToList();
    }

    public async Task SaveServiceHealthAsync(IEnumerable<ServiceHealthSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var snapshot in snapshots)
        {
            var existing = await db.ServiceHealthSnapshots.FirstOrDefaultAsync(s =>
                s.TenantId == snapshot.TenantId &&
                s.ServiceName == snapshot.ServiceName &&
                s.ReportDate == snapshot.ReportDate);

            if (existing != null)
            {
                existing.TenantName = snapshot.TenantName;
                existing.Status = snapshot.Status;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.ServiceHealthSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<ServiceHealthIssueSnapshot>> GetServiceHealthIssuesAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.ServiceHealthIssueSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(s => ids.Contains(s.TenantId));
        }
        // Only the most recent collection per tenant (prunes issues resolved since last run)
        var all = await query.ToListAsync();
        return all.LatestDateRowsPerKey(s => s.TenantId, s => s.ReportDate).ToList();
    }

    public async Task SaveServiceHealthIssuesAsync(IEnumerable<ServiceHealthIssueSnapshot> snapshots)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        foreach (var snapshot in snapshots)
        {
            var existing = await db.ServiceHealthIssueSnapshots.FirstOrDefaultAsync(s =>
                s.TenantId == snapshot.TenantId &&
                s.IssueId == snapshot.IssueId &&
                s.ReportDate == snapshot.ReportDate);

            if (existing != null)
            {
                existing.TenantName = snapshot.TenantName;
                existing.Title = snapshot.Title;
                existing.ServiceName = snapshot.ServiceName;
                existing.Classification = snapshot.Classification;
                existing.Status = snapshot.Status;
                existing.Feature = snapshot.Feature;
                existing.StartDateTime = snapshot.StartDateTime;
                existing.IsResolved = snapshot.IsResolved;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.ServiceHealthIssueSnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();
    }
}
