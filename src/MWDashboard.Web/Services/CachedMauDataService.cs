using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using MWDashboard.Shared.Models;
using MWDashboard.Shared.Services;

namespace MWDashboard.Web.Services;

/// <summary>
/// Caching decorator for IMauDataService. Caches read operations with Redis (or in-memory fallback).
/// Write operations pass through and invalidate relevant cache entries.
/// </summary>
public class CachedMauDataService : IMauDataService
{
    private readonly IMauDataService _inner;
    private readonly IDistributedCache _cache;
    private static readonly DistributedCacheEntryOptions CacheOptions15Min = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
    };
    private static readonly DistributedCacheEntryOptions CacheOptions60Min = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
    };

    public CachedMauDataService(IMauDataService inner, IDistributedCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    private static string BuildKey(string feature, IEnumerable<string>? tenantIds, params object[] extra)
    {
        var tenantPart = tenantIds == null ? "all" : string.Join("-", tenantIds.Order());
        var extraPart = extra.Length > 0 ? ":" + string.Join(":", extra) : "";
        return $"MWDashboard:{feature}:{tenantPart}{extraPart}";
    }

    private async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, DistributedCacheEntryOptions options)
    {
        try
        {
            var cached = await _cache.GetStringAsync(key);
            if (cached != null)
                return JsonSerializer.Deserialize<T>(cached)!;
        }
        catch
        {
            // Cache unavailable — fall through to direct query
        }

        var result = await factory();

        try
        {
            var json = JsonSerializer.Serialize(result);
            await _cache.SetStringAsync(key, json, options);
        }
        catch
        {
            // Cache write failure — non-critical
        }

        return result;
    }

    private async Task InvalidateAsync(params string[] keyPrefixes)
    {
        // Distributed cache doesn't support prefix-based invalidation natively.
        // For now, we remove known keys. In production with Redis, you could use SCAN.
        foreach (var prefix in keyPrefixes)
        {
            try { await _cache.RemoveAsync(prefix); } catch { }
        }
    }

    // --- Consumption (cached 15 min) ---
    public Task<List<ConsumptionSnapshot>> GetConsumptionAsync(IEnumerable<string>? tenantIds, int months = 6)
    {
        var key = BuildKey("consumption", tenantIds, months);
        return GetOrSetAsync(key, () => _inner.GetConsumptionAsync(tenantIds, months), CacheOptions15Min);
    }

    public async Task SaveConsumptionAsync(ConsumptionSnapshot snapshot)
    {
        await _inner.SaveConsumptionAsync(snapshot);
        await InvalidateAsync(BuildKey("consumption", null), BuildKey("consumption", new[] { snapshot.TenantId }));
    }

    // --- Storage (cached 15 min) ---
    public Task<List<StorageSnapshot>> GetStorageAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        var key = BuildKey("storage", tenantIds, days);
        return GetOrSetAsync(key, () => _inner.GetStorageAsync(tenantIds, days), CacheOptions15Min);
    }

    public async Task SaveStorageAsync(IEnumerable<StorageSnapshot> snapshots)
    {
        await _inner.SaveStorageAsync(snapshots);
        var tenantIds = snapshots.Select(s => s.TenantId).Distinct().ToArray();
        foreach (var tid in tenantIds)
            await InvalidateAsync(BuildKey("storage", new[] { tid }));
        await InvalidateAsync(BuildKey("storage", null));
    }

    // --- M365 App Usage (cached 15 min) ---
    public Task<List<M365AppUsageSnapshot>> GetM365AppUsageAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("m365app", tenantIds);
        return GetOrSetAsync(key, () => _inner.GetM365AppUsageAsync(tenantIds), CacheOptions15Min);
    }

    public async Task SaveM365AppUsageAsync(IEnumerable<M365AppUsageSnapshot> snapshots)
    {
        await _inner.SaveM365AppUsageAsync(snapshots);
        await InvalidateAsync(BuildKey("m365app", null));
    }

    // --- Pass-through for write operations; cached reads below ---

    // --- MAU History (15 min — dashboard-level, queried frequently) ---
    public Task<List<MauSnapshot>> GetMauHistoryAsync(string? tenantId, int months = 12)
    {
        var key = BuildKey("mau-history", tenantId != null ? new[] { tenantId } : null, months);
        return GetOrSetAsync(key, () => _inner.GetMauHistoryAsync(tenantId, months), CacheOptions15Min);
    }
    public Task<List<MauSnapshot>> GetMauHistoryAsync(IEnumerable<string>? tenantIds, int months = 12)
    {
        var key = BuildKey("mau-history", tenantIds, months);
        return GetOrSetAsync(key, () => _inner.GetMauHistoryAsync(tenantIds, months), CacheOptions15Min);
    }
    public Task<List<MauSnapshot>> GetLatestMauByServiceAsync(string? tenantId = null)
    {
        var key = BuildKey("mau-latest", tenantId != null ? new[] { tenantId } : null);
        return GetOrSetAsync(key, () => _inner.GetLatestMauByServiceAsync(tenantId), CacheOptions15Min);
    }
    public Task<List<MauSnapshot>> GetLatestMauByServiceAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("mau-latest", tenantIds);
        return GetOrSetAsync(key, () => _inner.GetLatestMauByServiceAsync(tenantIds), CacheOptions15Min);
    }
    public async Task SaveSnapshotsAsync(IEnumerable<MauSnapshot> snapshots)
    {
        await _inner.SaveSnapshotsAsync(snapshots);
        await InvalidateAsync(BuildKey("mau-history", null), BuildKey("mau-latest", null));
    }

    // --- Licenses (60 min — changes daily) ---
    public Task<List<LicenseSnapshot>> GetLatestLicensesAsync(string? tenantId = null)
    {
        var key = BuildKey("licenses-latest", tenantId != null ? new[] { tenantId } : null);
        return GetOrSetAsync(key, () => _inner.GetLatestLicensesAsync(tenantId), CacheOptions60Min);
    }
    public Task<List<LicenseSnapshot>> GetLatestLicensesAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("licenses-latest", tenantIds);
        return GetOrSetAsync(key, () => _inner.GetLatestLicensesAsync(tenantIds), CacheOptions60Min);
    }
    public Task<List<LicenseSnapshot>> GetLicensesByDateRangeAsync(DateTime from, DateTime to, string? tenantId = null)
    {
        var key = BuildKey("licenses-range", tenantId != null ? new[] { tenantId } : null, from.ToString("yyyyMMdd"), to.ToString("yyyyMMdd"));
        return GetOrSetAsync(key, () => _inner.GetLicensesByDateRangeAsync(from, to, tenantId), CacheOptions60Min);
    }
    public Task<List<LicenseSnapshot>> GetLicensesByDateRangeAsync(DateTime from, DateTime to, IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("licenses-range", tenantIds, from.ToString("yyyyMMdd"), to.ToString("yyyyMMdd"));
        return GetOrSetAsync(key, () => _inner.GetLicensesByDateRangeAsync(from, to, tenantIds), CacheOptions60Min);
    }
    public async Task SaveLicensesAsync(IEnumerable<LicenseSnapshot> licenses)
    {
        await _inner.SaveLicensesAsync(licenses);
        await InvalidateAsync(BuildKey("licenses-latest", null), BuildKey("licenses-range", null));
    }
    public Task<(DateTime? Earliest, DateTime? Latest)> GetLicenseDataRangeAsync()
        => _inner.GetLicenseDataRangeAsync(); // cheap scalar query, no cache needed

    // --- Message Center Posts (60 min — infrequent updates) ---
    public Task<List<MessageCenterPost>> GetMessageCenterPostsAsync(string? tenantId = null)
    {
        var key = BuildKey("msgcenter", tenantId != null ? new[] { tenantId } : null);
        return GetOrSetAsync(key, () => _inner.GetMessageCenterPostsAsync(tenantId), CacheOptions60Min);
    }
    public Task<List<MessageCenterPost>> GetMessageCenterPostsAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("msgcenter", tenantIds);
        return GetOrSetAsync(key, () => _inner.GetMessageCenterPostsAsync(tenantIds), CacheOptions60Min);
    }
    public async Task SaveMessageCenterPostsAsync(IEnumerable<MessageCenterPost> posts)
    {
        await _inner.SaveMessageCenterPostsAsync(posts);
        await InvalidateAsync(BuildKey("msgcenter", null));
    }

    // --- Security (15 min — dashboard-level) ---
    public Task<List<SecuritySignInSummary>> GetSecuritySummaryAsync(string? tenantId, int days = 30)
    {
        var key = BuildKey("security", tenantId != null ? new[] { tenantId } : null, days);
        return GetOrSetAsync(key, () => _inner.GetSecuritySummaryAsync(tenantId, days), CacheOptions15Min);
    }
    public Task<List<SecuritySignInSummary>> GetSecuritySummaryAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        var key = BuildKey("security", tenantIds, days);
        return GetOrSetAsync(key, () => _inner.GetSecuritySummaryAsync(tenantIds, days), CacheOptions15Min);
    }
    public async Task SaveSecuritySummariesAsync(IEnumerable<SecuritySignInSummary> summaries)
    {
        await _inner.SaveSecuritySummariesAsync(summaries);
        await InvalidateAsync(BuildKey("security", null));
    }
    public Task<List<TenantEntraTier>> GetTenantEntraIdTiersAsync()
    {
        var key = "MWDashboard:entra-tiers:all";
        return GetOrSetAsync(key, () => _inner.GetTenantEntraIdTiersAsync(), CacheOptions60Min);
    }

    // --- Workload Activity (15 min — dashboard-level) ---
    public Task<List<WorkloadActivitySnapshot>> GetWorkloadActivityAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        var key = BuildKey("activity", tenantIds, days);
        return GetOrSetAsync(key, () => _inner.GetWorkloadActivityAsync(tenantIds, days), CacheOptions15Min);
    }
    public async Task SaveWorkloadActivityAsync(IEnumerable<WorkloadActivitySnapshot> activities)
    {
        await _inner.SaveWorkloadActivityAsync(activities);
        await InvalidateAsync(BuildKey("activity", null));
    }

    // --- Copilot (60 min — changes daily) ---
    public Task<List<CopilotUsageSnapshot>> GetCopilotUsageAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("copilot", tenantIds);
        return GetOrSetAsync(key, () => _inner.GetCopilotUsageAsync(tenantIds), CacheOptions60Min);
    }
    public async Task SaveCopilotUsageAsync(IEnumerable<CopilotUsageSnapshot> snapshots)
    {
        await _inner.SaveCopilotUsageAsync(snapshots);
        await InvalidateAsync(BuildKey("copilot", null));
    }

    // --- User Segmentation (60 min — changes daily) ---
    public Task<List<UserSegmentSnapshot>> GetUserSegmentsAsync(IEnumerable<string>? tenantIds, int months = 6)
    {
        var key = BuildKey("segments", tenantIds, months);
        return GetOrSetAsync(key, () => _inner.GetUserSegmentsAsync(tenantIds, months), CacheOptions60Min);
    }
    public async Task SaveUserSegmentsAsync(IEnumerable<UserSegmentSnapshot> segments)
    {
        await _inner.SaveUserSegmentsAsync(segments);
        await InvalidateAsync(BuildKey("segments", null));
    }

    // --- Department Usage (60 min — changes daily) ---
    public Task<List<DepartmentUsageSnapshot>> GetDepartmentUsageAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("departments", tenantIds);
        return GetOrSetAsync(key, () => _inner.GetDepartmentUsageAsync(tenantIds), CacheOptions60Min);
    }
    public async Task SaveDepartmentUsageAsync(IEnumerable<DepartmentUsageSnapshot> snapshots)
    {
        await _inner.SaveDepartmentUsageAsync(snapshots);
        await InvalidateAsync(BuildKey("departments", null));
    }
}
