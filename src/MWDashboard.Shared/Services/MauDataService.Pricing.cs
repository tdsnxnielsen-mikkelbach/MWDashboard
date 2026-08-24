using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public partial class MauDataService
{
    // --- License pricing (fused: PCT list price + Ion cost/margin) ---

    /// <summary>Delete-then-insert the day's fused-pricing rows for one tenant (idempotent per (TenantId, ReportDate)).</summary>
    public async Task SaveLicensePricingAsync(string tenantId, DateTime reportDate, IEnumerable<LicensePricingSnapshot> rows)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var date = reportDate.Date;
        await db.LicensePricingSnapshots
            .Where(r => r.TenantId == tenantId && r.ReportDate == date)
            .ExecuteDeleteAsync();

        var list = rows.ToList();
        foreach (var r in list) { r.ReportDate = date; r.CollectedAt = DateTime.UtcNow; }
        if (list.Count > 0)
        {
            db.LicensePricingSnapshots.AddRange(list);
            await db.SaveChangesAsync();
        }
    }

    public async Task<List<LicensePricingSnapshot>> GetLatestLicensePricingAsync(IEnumerable<string>? tenantIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.LicensePricingSnapshots.AsNoTracking().AsQueryable();
        if (tenantIds != null)
        {
            var ids = tenantIds.ToList();
            query = query.Where(r => ids.Contains(r.TenantId));
        }

        // Latest ReportDate per tenant, then all rows for that date.
        var latestDates = query
            .GroupBy(r => r.TenantId)
            .Select(g => new { TenantId = g.Key, MaxDate = g.Max(r => r.ReportDate) });

        return await query
            .Where(r => latestDates.Any(d => d.TenantId == r.TenantId && d.MaxDate == r.ReportDate))
            .ToListAsync();
    }

    // --- Reseller directory ---

    /// <summary>Upsert resellers by <see cref="IonReseller.AccountId"/>. Returns the number of rows written.</summary>
    public async Task<int> SaveResellersAsync(IEnumerable<IonReseller> resellers)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var incoming = resellers.ToList();
        if (incoming.Count == 0) return 0;

        var ids = incoming.Select(r => r.AccountId).ToList();
        var existing = await db.IonResellers.Where(r => ids.Contains(r.AccountId)).ToDictionaryAsync(r => r.AccountId);

        foreach (var r in incoming)
        {
            if (existing.TryGetValue(r.AccountId, out var e))
            {
                e.Name = r.Name;
                e.PrimaryContactEmail = r.PrimaryContactEmail;
                e.DelegatedAdminEnabled = r.DelegatedAdminEnabled;
                e.CreatedTime = r.CreatedTime;
                if (!string.IsNullOrEmpty(r.MpnId)) e.MpnId = r.MpnId;
                if (!string.IsNullOrEmpty(r.PctResellerId)) e.PctResellerId = r.PctResellerId;
                e.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                r.UpdatedAt = DateTime.UtcNow;
                db.IonResellers.Add(r);
            }
        }
        await db.SaveChangesAsync();
        return incoming.Count;
    }

    public async Task<List<IonReseller>> GetResellersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.IonResellers.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
    }

    // --- Reseller realised pricing (from crawled orders) ---

    /// <summary>Upsert reseller order pricing per (IonAccountId, IonCustomerId, MfgPartNumber, ReportDate).</summary>
    public async Task SaveResellerPricingAsync(DateTime reportDate, IEnumerable<ResellerPricingSnapshot> rows)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var date = reportDate.Date;
        var list = rows.ToList();
        if (list.Count == 0) return;

        var accounts = list.Select(r => r.IonAccountId).Distinct().ToList();
        await db.ResellerPricingSnapshots
            .Where(r => accounts.Contains(r.IonAccountId) && r.ReportDate == date)
            .ExecuteDeleteAsync();

        foreach (var r in list) { r.ReportDate = date; r.CollectedAt = DateTime.UtcNow; }
        db.ResellerPricingSnapshots.AddRange(list);
        await db.SaveChangesAsync();
    }

    public async Task<List<ResellerPricingSnapshot>> GetLatestResellerPricingAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (!await db.ResellerPricingSnapshots.AnyAsync()) return [];
        var maxDate = await db.ResellerPricingSnapshots.MaxAsync(r => r.ReportDate);
        return await db.ResellerPricingSnapshots.AsNoTracking()
            .Where(r => r.ReportDate == maxDate)
            .ToListAsync();
    }

    // --- ION customer onboarding (pricing-only tenants) ---

    /// <summary>
    /// Upsert customers discovered in the Ion Gateway directory. New tenant ids are added as
    /// <b>pricing-only</b> records (<see cref="TenantInfo.PricingOnly"/> = true, <see cref="TenantInfo.IsActive"/>
    /// = false) so they never enter Graph collection or portal sign-in; existing (native, consented) tenants
    /// are only annotated with their reseller/account info and never demoted. Returns (added, annotated).
    /// </summary>
    public async Task<(int Added, int Annotated)> UpsertIonCustomersAsync(IEnumerable<IonDirectoryCustomer> customers)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var incoming = customers
            .Where(c => !string.IsNullOrWhiteSpace(c.TenantId))
            .GroupBy(c => c.TenantId!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (incoming.Count == 0) return (0, 0);

        var ids = incoming.Select(c => c.TenantId!).ToList();
        var existing = await db.Tenants.Where(t => ids.Contains(t.TenantId)).ToListAsync();
        var byId = existing.ToDictionary(t => t.TenantId, StringComparer.OrdinalIgnoreCase);

        int added = 0, annotated = 0;
        foreach (var c in incoming)
        {
            var name = string.IsNullOrWhiteSpace(c.Name) ? c.Domain ?? c.TenantId! : c.Name!;
            if (byId.TryGetValue(c.TenantId!, out var t))
            {
                // Annotate existing tenant (native or already-imported) — never demote a native tenant.
                t.IonAccountId = c.IonAccountId ?? t.IonAccountId;
                t.ResellerName = string.IsNullOrWhiteSpace(c.ResellerName) ? t.ResellerName : c.ResellerName!;
                if (t.Source != "Native" && !string.IsNullOrWhiteSpace(c.Source))
                    t.Source = char.ToUpperInvariant(c.Source![0]) + c.Source[1..].ToLowerInvariant();
                annotated++;
            }
            else
            {
                db.Tenants.Add(new TenantInfo
                {
                    TenantId = c.TenantId!,
                    TenantName = name,
                    DisplayName = string.IsNullOrWhiteSpace(c.Domain) ? name : c.Domain!,
                    IsActive = false,
                    PricingOnly = true,
                    Source = "Ion",
                    IonAccountId = c.IonAccountId,
                    ResellerName = c.ResellerName ?? string.Empty,
                    OnboardedAt = DateTime.UtcNow
                });
                added++;
            }
        }
        await db.SaveChangesAsync();
        return (added, annotated);
    }

    /// <summary>All customers linked to a reseller (imported pricing-only from ION/PCT + native tenants with an Ion account).</summary>
    public async Task<List<TenantInfo>> GetIonCustomersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Source != "Native" || t.IonAccountId != null)
            .OrderBy(t => t.TenantName)
            .ToListAsync();
    }
}
