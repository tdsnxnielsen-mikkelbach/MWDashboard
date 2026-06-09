---
name: new-model
description: "Add a new snapshot model to MWDashboard. Use when adding a new data entity, DbSet, migration, data service methods (save + query), and cache integration for a new metric type."
argument-hint: "Model name and fields (e.g., 'ComplianceSnapshot with DeviceCount, CompliantCount, NonCompliantCount')"
---

# Add a New Snapshot Model

## When to Use
- Adding a new data collection metric or entity to the system
- User asks to store a new type of Graph API data

## Procedure

### 1. Define the entity model

File: `src/MWDashboard.Shared/Models/MauSnapshot.cs` (all models live in this file)

Follow the standard pattern:

```csharp
public class {Name}Snapshot
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    // Add metric-specific fields here (e.g., string discriminator + numeric values)
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
```

Rules:
- All entities start with `Id`, `TenantId`, `TenantName`, `ReportDate`
- All entities end with `CollectedAt` defaulting to `DateTime.UtcNow`
- Use `string` for discriminator fields (ServiceName, AppName, Department, etc.)
- Use `int` or `long` for counts, `double` for ratios/percentages
- Use `DateTime` for all dates (UTC everywhere)

### 2. Add DbSet to MauDbContext

File: `src/MWDashboard.Shared/Data/MauDbContext.cs`

```csharp
public DbSet<{Name}Snapshot> {Name}Snapshots => Set<{Name}Snapshot>();
```

### 3. Configure entity in OnModelCreating

Add a composite unique index for upsert deduplication:

```csharp
modelBuilder.Entity<{Name}Snapshot>(entity =>
{
    entity.HasIndex(e => new { e.TenantId, /* discriminator fields */, e.ReportDate }).IsUnique();
    entity.Property(e => e.TenantId).HasMaxLength(100);
    entity.Property(e => e.TenantName).HasMaxLength(250);
    // HasMaxLength(100) for short discriminators, 250 for names/descriptions
});
```

Index composition patterns from existing models:
- Per-service: `{ TenantId, ServiceName, ReportDate }`
- Per-workload+activity: `{ TenantId, Workload, ActivityType, ReportDate }`
- Per-app: `{ TenantId, AppName, ReportDate }`
- Per-app+platform: `{ TenantId, AppName, Platform, ReportDate }`
- Per-department: `{ TenantId, Department, ReportDate }`
- Tenant-level (no discriminator): `{ TenantId, ReportDate }`

### 4. Add EF Core migration

Run from `src/MWDashboard.Web/`:

```powershell
dotnet ef migrations add Add{Name}Snapshot --project ../MWDashboard.Shared
```

### 5. Add data service methods

File: `src/MWDashboard.Shared/Services/MauDataService.cs`

Add to `IMauDataService` interface:

```csharp
Task<List<{Name}Snapshot>> Get{Name}Async(IEnumerable<string>? tenantIds, int days = 30);
Task Save{Name}Async(List<{Name}Snapshot> snapshots);
```

Implement in `MauDataService`:

```csharp
public async Task<List<{Name}Snapshot>> Get{Name}Async(IEnumerable<string>? tenantIds, int days = 30)
{
    await using var db = await _dbFactory.CreateDbContextAsync();
    var cutoff = DateTime.UtcNow.AddDays(-days);
    var query = db.{Name}Snapshots.AsNoTracking().Where(s => s.ReportDate >= cutoff);
    if (tenantIds != null)
    {
        var ids = tenantIds.ToList();
        query = query.Where(s => ids.Contains(s.TenantId));
    }
    return await query.OrderBy(s => s.ReportDate).ToListAsync();
}

public async Task Save{Name}Async(List<{Name}Snapshot> snapshots)
{
    if (snapshots.Count == 0) return;
    await using var db = await _dbFactory.CreateDbContextAsync();
    foreach (var snapshot in snapshots)
    {
        var existing = await db.{Name}Snapshots.FirstOrDefaultAsync(s =>
            s.TenantId == snapshot.TenantId &&
            /* match discriminator fields */ &&
            s.ReportDate == snapshot.ReportDate);
        if (existing != null)
        {
            // Update metric fields
            existing.CollectedAt = DateTime.UtcNow;
        }
        else
        {
            db.{Name}Snapshots.Add(snapshot);
        }
    }
    await db.SaveChangesAsync();
}
```

### 6. Add cache wrapper methods

File: `src/MWDashboard.Web/Services/CachedMauDataService.cs`

```csharp
public async Task<List<{Name}Snapshot>> Get{Name}Async(IEnumerable<string>? tenantIds, int days = 30)
{
    var tenantKey = tenantIds != null ? string.Join(",", tenantIds.OrderBy(t => t)) : "all";
    var cacheKey = $"MWDashboard:{feature}:{tenantKey}:{days}d";
    // Use 15-min TTL for dashboard-level, 60-min for daily-changing data
    return await GetOrSetAsync(cacheKey, () => _inner.Get{Name}Async(tenantIds, days), TimeSpan.FromMinutes(15));
}

public async Task Save{Name}Async(List<{Name}Snapshot> snapshots)
{
    await _inner.Save{Name}Async(snapshots);
    // Invalidate relevant cache keys
    await InvalidatePrefixAsync("MWDashboard:{feature}:");
}
```

### 7. Checklist

- [ ] Entity class added to `MauSnapshot.cs` with standard field pattern
- [ ] DbSet added to `MauDbContext`
- [ ] Composite unique index configured in `OnModelCreating`
- [ ] EF Core migration created and applied
- [ ] `IMauDataService` interface updated with Get + Save methods
- [ ] `MauDataService` implements Get (with `AsNoTracking`) and Save (upsert pattern)
- [ ] `CachedMauDataService` wraps both methods with appropriate TTL
- [ ] Save method invalidates cache
