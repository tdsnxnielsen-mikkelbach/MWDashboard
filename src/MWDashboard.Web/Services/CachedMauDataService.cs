using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using MWDashboard.Shared.Models;
using MWDashboard.Shared.Services;

namespace MWDashboard.Web.Services;

/// <summary>
/// Caching decorator for IMauDataService. Caches read operations with Redis (or in-memory fallback).
/// Write operations pass through and invalidate relevant cache entries.
/// Supports sliding+absolute expiration, multi-tenant combo caching, and cross-replica pub/sub invalidation.
/// </summary>
public class CachedMauDataService : IMauDataService
{
    private readonly IMauDataService _inner;
    private readonly IDistributedCache _cache;
    private readonly RedisCacheInvalidationService? _invalidationService;

    // Sliding within absolute: active dashboards stay warm, idle ones expire
    private static readonly DistributedCacheEntryOptions CacheOptions15Min = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15),
        SlidingExpiration = TimeSpan.FromMinutes(5)
    };
    private static readonly DistributedCacheEntryOptions CacheOptions60Min = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60),
        SlidingExpiration = TimeSpan.FromMinutes(20)
    };
    // Short TTL for multi-tenant combos (no explicit invalidation needed)
    private static readonly DistributedCacheEntryOptions CacheOptionsShort = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(4)
    };

    public CachedMauDataService(IMauDataService inner, IDistributedCache cache, RedisCacheInvalidationService? invalidationService = null)
    {
        _inner = inner;
        _cache = cache;
        _invalidationService = invalidationService;
    }

    private static string BuildKey(string feature, IEnumerable<string>? tenantIds, params object[] extra)
    {
        var tenantPart = tenantIds == null ? "all" : string.Join("-", tenantIds.Order());
        var extraPart = extra.Length > 0 ? ":" + string.Join(":", extra) : "";
        return $"MWDashboard:{feature}:{tenantPart}{extraPart}";
    }

    private static bool IsMultiTenantCombo(IEnumerable<string>? tenantIds)
    {
        if (tenantIds == null) return false;
        using var enumerator = tenantIds.GetEnumerator();
        if (!enumerator.MoveNext()) return false; // empty
        return enumerator.MoveNext(); // true if 2+ items
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

        // Never cache empty collection results. Otherwise a query run before data is
        // collected (e.g. by the startup cache warm-up) would poison the key with an
        // empty list for the full TTL — and collection that happens in a separate
        // process (Collector container / scheduled Job, which use the non-caching data
        // service) never invalidates it. This made all-/multi-tenant views show "no data"
        // while single-tenant views (never warmed) queried fresh and worked.
        if (result is System.Collections.ICollection { Count: 0 })
            return result;

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
        foreach (var prefix in keyPrefixes)
        {
            try { await _cache.RemoveAsync(prefix); } catch { }
        }

        // Publish to other replicas via Redis pub/sub
        if (_invalidationService != null)
            await _invalidationService.PublishInvalidationAsync(keyPrefixes);
    }

    // --- Consumption (cached 15 min) ---
    public Task<List<ConsumptionSnapshot>> GetConsumptionAsync(IEnumerable<string>? tenantIds, int months = 6)
    {
        var key = BuildKey("consumption", tenantIds, months);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions15Min;
        return GetOrSetAsync(key, () => _inner.GetConsumptionAsync(tenantIds, months), options);
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
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions15Min;
        return GetOrSetAsync(key, () => _inner.GetStorageAsync(tenantIds, days), options);
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
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions15Min;
        return GetOrSetAsync(key, () => _inner.GetM365AppUsageAsync(tenantIds), options);
    }

    public async Task SaveM365AppUsageAsync(IEnumerable<M365AppUsageSnapshot> snapshots)
    {
        await _inner.SaveM365AppUsageAsync(snapshots);
        await InvalidateAsync(BuildKey("m365app", null));
    }

    // --- M365 App per-user detail + Office activations (cached 60 min — daily-changing data) ---
    public Task<List<M365AppUserDetailSnapshot>> GetM365AppUserDetailAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("m365appdetail", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetM365AppUserDetailAsync(tenantIds), options);
    }

    public async Task SaveM365AppUserDetailAsync(IEnumerable<M365AppUserDetailSnapshot> snapshots)
    {
        await _inner.SaveM365AppUserDetailAsync(snapshots);
        await InvalidateAsync(BuildKey("m365appdetail", null));
    }

    public Task<List<Office365ActivationSnapshot>> GetOffice365ActivationsAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("office-activations", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetOffice365ActivationsAsync(tenantIds), options);
    }

    public async Task SaveOffice365ActivationsAsync(IEnumerable<Office365ActivationSnapshot> snapshots)
    {
        await _inner.SaveOffice365ActivationsAsync(snapshots);
        await InvalidateAsync(BuildKey("office-activations", null));
    }

    public Task<List<Office365ActivationUserSnapshot>> GetOffice365ActivationUsersAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("office-activation-users", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetOffice365ActivationUsersAsync(tenantIds), options);
    }

    public async Task SaveOffice365ActivationUsersAsync(IEnumerable<Office365ActivationUserSnapshot> snapshots)
    {
        await _inner.SaveOffice365ActivationUsersAsync(snapshots);
        await InvalidateAsync(BuildKey("office-activation-users", null));
    }

    // --- Secure Score (cached 60 min — daily-changing data) ---
    public Task<List<SecureScoreSnapshot>> GetSecureScoresAsync(IEnumerable<string>? tenantIds, int days = 90)
    {
        var key = BuildKey("securescore", tenantIds, days);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetSecureScoresAsync(tenantIds, days), options);
    }

    public async Task SaveSecureScoresAsync(IEnumerable<SecureScoreSnapshot> snapshots)
    {
        await _inner.SaveSecureScoresAsync(snapshots);
        var tenantIds = snapshots.Select(s => s.TenantId).Distinct().ToArray();
        foreach (var tid in tenantIds)
            await InvalidateAsync(BuildKey("securescore", new[] { tid }));
        await InvalidateAsync(BuildKey("securescore", null));
    }

    public Task<List<SecureScoreControlSnapshot>> GetSecureScoreControlsAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("securescore-controls", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetSecureScoreControlsAsync(tenantIds), options);
    }

    public async Task SaveSecureScoreControlsAsync(IEnumerable<SecureScoreControlSnapshot> snapshots)
    {
        await _inner.SaveSecureScoreControlsAsync(snapshots);
        var tenantIds = snapshots.Select(s => s.TenantId).Distinct().ToArray();
        foreach (var tid in tenantIds)
            await InvalidateAsync(BuildKey("securescore-controls", new[] { tid }));
        await InvalidateAsync(BuildKey("securescore-controls", null));
    }

    // --- MFA Registration (cached 60 min — daily-changing data) ---
    public Task<List<MfaRegistrationSnapshot>> GetMfaRegistrationAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("mfa-registration", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetMfaRegistrationAsync(tenantIds), options);
    }

    public async Task SaveMfaRegistrationAsync(MfaRegistrationSnapshot snapshot)
    {
        await _inner.SaveMfaRegistrationAsync(snapshot);
        await InvalidateAsync(BuildKey("mfa-registration", new[] { snapshot.TenantId }), BuildKey("mfa-registration", null));
    }

    // --- Inactive Accounts (cached 60 min — daily-changing data) ---
    public Task<List<InactiveAccountSnapshot>> GetInactiveAccountsAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("inactive-accounts", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetInactiveAccountsAsync(tenantIds), options);
    }

    public async Task SaveInactiveAccountsAsync(InactiveAccountSnapshot snapshot)
    {
        await _inner.SaveInactiveAccountsAsync(snapshot);
        await InvalidateAsync(BuildKey("inactive-accounts", new[] { snapshot.TenantId }), BuildKey("inactive-accounts", null));
    }

    // --- Service Health (cached 15 min — operational status changes more often) ---
    public Task<List<ServiceHealthSnapshot>> GetServiceHealthAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("service-health", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions15Min;
        return GetOrSetAsync(key, () => _inner.GetServiceHealthAsync(tenantIds), options);
    }

    public async Task SaveServiceHealthAsync(IEnumerable<ServiceHealthSnapshot> snapshots)
    {
        await _inner.SaveServiceHealthAsync(snapshots);
        var tenantIds = snapshots.Select(s => s.TenantId).Distinct().ToArray();
        foreach (var tid in tenantIds)
            await InvalidateAsync(BuildKey("service-health", new[] { tid }));
        await InvalidateAsync(BuildKey("service-health", null));
    }

    public Task<List<ServiceHealthIssueSnapshot>> GetServiceHealthIssuesAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("service-health-issues", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions15Min;
        return GetOrSetAsync(key, () => _inner.GetServiceHealthIssuesAsync(tenantIds), options);
    }

    public async Task SaveServiceHealthIssuesAsync(IEnumerable<ServiceHealthIssueSnapshot> snapshots)
    {
        await _inner.SaveServiceHealthIssuesAsync(snapshots);
        var tenantIds = snapshots.Select(s => s.TenantId).Distinct().ToArray();
        foreach (var tid in tenantIds)
            await InvalidateAsync(BuildKey("service-health-issues", new[] { tid }));
        await InvalidateAsync(BuildKey("service-health-issues", null));
    }

    // --- Device Compliance (cached 60 min — daily-changing data) ---
    public Task<List<DeviceComplianceSnapshot>> GetDeviceComplianceAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("device-compliance", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetDeviceComplianceAsync(tenantIds), options);
    }

    public async Task SaveDeviceComplianceAsync(DeviceComplianceSnapshot snapshot)
    {
        await _inner.SaveDeviceComplianceAsync(snapshot);
        await InvalidateAsync(BuildKey("device-compliance", new[] { snapshot.TenantId }), BuildKey("device-compliance", null));
    }

    // --- Device patch / OS-version hygiene (cached 60 min — daily-changing data) ---
    public Task<List<DevicePatchSnapshot>> GetDevicePatchAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("device-patch", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetDevicePatchAsync(tenantIds), options);
    }

    public Task<List<DevicePatchSnapshot>> GetDevicePatchHistoryAsync(IEnumerable<string>? tenantIds, int days = 90)
    {
        var key = BuildKey("device-patch-history", tenantIds, days);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetDevicePatchHistoryAsync(tenantIds, days), options);
    }

    public async Task SaveDevicePatchAsync(string tenantId, DateTime reportDate, IEnumerable<DevicePatchSnapshot> snapshots)
    {
        await _inner.SaveDevicePatchAsync(tenantId, reportDate, snapshots);
        await InvalidateAsync(BuildKey("device-patch", new[] { tenantId }), BuildKey("device-patch", null));
    }

    // --- Conditional Access (cached 60 min — daily-changing data) ---
    public Task<List<ConditionalAccessSnapshot>> GetConditionalAccessAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("conditional-access", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetConditionalAccessAsync(tenantIds), options);
    }

    public async Task SaveConditionalAccessAsync(ConditionalAccessSnapshot snapshot)
    {
        await _inner.SaveConditionalAccessAsync(snapshot);
        await InvalidateAsync(BuildKey("conditional-access", new[] { snapshot.TenantId }), BuildKey("conditional-access", null));
    }

    // --- Guest Users (cached 60 min — daily-changing data) ---
    public Task<List<GuestUserSnapshot>> GetGuestUsersAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("guest-users", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetGuestUsersAsync(tenantIds), options);
    }

    public async Task SaveGuestUsersAsync(GuestUserSnapshot snapshot)
    {
        await _inner.SaveGuestUsersAsync(snapshot);
        await InvalidateAsync(BuildKey("guest-users", new[] { snapshot.TenantId }), BuildKey("guest-users", null));
    }

    // --- Risky Users (cached 60 min — daily-changing data) ---
    public Task<List<RiskyUserSnapshot>> GetRiskyUsersAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("risky-users", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetRiskyUsersAsync(tenantIds), options);
    }

    public async Task SaveRiskyUsersAsync(RiskyUserSnapshot snapshot)
    {
        await _inner.SaveRiskyUsersAsync(snapshot);
        await InvalidateAsync(BuildKey("risky-users", new[] { snapshot.TenantId }), BuildKey("risky-users", null));
    }

    // --- Mailbox Usage (Tier 3, cached 60 min — daily-changing data) ---
    public Task<List<MailboxUsageSnapshot>> GetMailboxUsageAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("mailbox-usage", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetMailboxUsageAsync(tenantIds), options);
    }

    public async Task SaveMailboxUsageAsync(MailboxUsageSnapshot snapshot)
    {
        await _inner.SaveMailboxUsageAsync(snapshot);
        await InvalidateAsync(BuildKey("mailbox-usage", new[] { snapshot.TenantId }), BuildKey("mailbox-usage", null));
    }

    public Task<List<TopMailboxSnapshot>> GetTopMailboxesAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("top-mailboxes", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetTopMailboxesAsync(tenantIds), options);
    }

    public async Task SaveTopMailboxesAsync(IEnumerable<TopMailboxSnapshot> snapshots)
    {
        await _inner.SaveTopMailboxesAsync(snapshots);
        var tenantId = snapshots.Select(s => s.TenantId).FirstOrDefault();
        if (tenantId != null)
            await InvalidateAsync(BuildKey("top-mailboxes", new[] { tenantId }), BuildKey("top-mailboxes", null));
    }

    // --- Teams Device Usage (Tier 3, cached 60 min) ---
    public Task<List<TeamsDeviceUsageSnapshot>> GetTeamsDeviceUsageAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("teams-device-usage", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetTeamsDeviceUsageAsync(tenantIds), options);
    }

    public async Task SaveTeamsDeviceUsageAsync(TeamsDeviceUsageSnapshot snapshot)
    {
        await _inner.SaveTeamsDeviceUsageAsync(snapshot);
        await InvalidateAsync(BuildKey("teams-device-usage", new[] { snapshot.TenantId }), BuildKey("teams-device-usage", null));
    }

    // --- SharePoint / OneDrive Site Usage (Tier 3, cached 60 min) ---
    public Task<List<SiteUsageSnapshot>> GetSiteUsageAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("site-usage", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetSiteUsageAsync(tenantIds), options);
    }

    public async Task SaveSiteUsageAsync(IEnumerable<SiteUsageSnapshot> snapshots)
    {
        await _inner.SaveSiteUsageAsync(snapshots);
        var tenantId = snapshots.Select(s => s.TenantId).FirstOrDefault();
        if (tenantId != null)
            await InvalidateAsync(BuildKey("site-usage", new[] { tenantId }), BuildKey("site-usage", null));
    }

    public Task<List<SiteUsageDetailSnapshot>> GetSiteUsageDetailAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("site-usage-detail", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetSiteUsageDetailAsync(tenantIds), options);
    }

    public async Task SaveSiteUsageDetailAsync(IEnumerable<SiteUsageDetailSnapshot> snapshots)
    {
        await _inner.SaveSiteUsageDetailAsync(snapshots);
        var tenantId = snapshots.Select(s => s.TenantId).FirstOrDefault();
        if (tenantId != null)
            await InvalidateAsync(BuildKey("site-usage-detail", new[] { tenantId }), BuildKey("site-usage-detail", null));
    }

    // --- Viva Engage / Yammer (Tier 3, cached 60 min) ---
    public Task<List<YammerActivitySnapshot>> GetYammerActivityAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("yammer-activity", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetYammerActivityAsync(tenantIds), options);
    }

    public async Task SaveYammerActivityAsync(YammerActivitySnapshot snapshot)
    {
        await _inner.SaveYammerActivityAsync(snapshot);
        await InvalidateAsync(BuildKey("yammer-activity", new[] { snapshot.TenantId }), BuildKey("yammer-activity", null));
    }

    // --- Groups & Teams sprawl (Tier 3, cached 60 min) ---
    public Task<List<GroupSnapshot>> GetGroupSprawlAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("group-sprawl", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetGroupSprawlAsync(tenantIds), options);
    }

    public async Task SaveGroupSprawlAsync(GroupSnapshot snapshot)
    {
        await _inner.SaveGroupSprawlAsync(snapshot);
        await InvalidateAsync(BuildKey("group-sprawl", new[] { snapshot.TenantId }), BuildKey("group-sprawl", null));
    }

    // --- Tenant consent health (no caching — written during collection, read directly from DB) ---
    public Task UpdateTenantPermissionStatusAsync(string tenantId, IEnumerable<string> missingPermissions)
        => _inner.UpdateTenantPermissionStatusAsync(tenantId, missingPermissions);

    // --- Copilot Chat (unlicensed) usage (60 min — daily-changing audit aggregate) ---
    public Task<List<CopilotChatUsageSnapshot>> GetCopilotChatUsageAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        var key = BuildKey("copilot-chat", tenantIds, days);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetCopilotChatUsageAsync(tenantIds, days), options);
    }

    public async Task SaveCopilotChatUsageAsync(IEnumerable<CopilotChatUsageSnapshot> snapshots)
    {
        await _inner.SaveCopilotChatUsageAsync(snapshots);
        foreach (var tid in snapshots.Select(s => s.TenantId).Distinct())
            await InvalidateAsync(BuildKey("copilot-chat", new[] { tid }));
        await InvalidateAsync(BuildKey("copilot-chat", null));
    }

    // The audit cursor is collection state, never cached — always read/written directly.
    public Task<DateTime?> GetCopilotAuditCursorAsync(string tenantId)
        => _inner.GetCopilotAuditCursorAsync(tenantId);

    public Task UpdateCopilotAuditCursorAsync(string tenantId, DateTime cursorUtc)
        => _inner.UpdateCopilotAuditCursorAsync(tenantId, cursorUtc);

    // --- App registration / service-principal credential expiry (60 min — daily-changing) ---
    public Task<List<AppCredentialSnapshot>> GetAppCredentialsAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("app-credentials", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetAppCredentialsAsync(tenantIds), options);
    }

    public async Task SaveAppCredentialsAsync(string tenantId, DateTime reportDate, IEnumerable<AppCredentialSnapshot> snapshots)
    {
        await _inner.SaveAppCredentialsAsync(tenantId, reportDate, snapshots);
        await InvalidateAsync(BuildKey("app-credentials", new[] { tenantId }), BuildKey("app-credentials", null));
    }

    // --- External sharing audit (60 min — daily-changing audit aggregate) ---
    public Task<List<ExternalSharingSnapshot>> GetExternalSharingAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        var key = BuildKey("external-sharing", tenantIds, days);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetExternalSharingAsync(tenantIds, days), options);
    }

    public async Task SaveExternalSharingAsync(IEnumerable<ExternalSharingSnapshot> snapshots)
    {
        await _inner.SaveExternalSharingAsync(snapshots);
        foreach (var tid in snapshots.Select(s => s.TenantId).Distinct())
            await InvalidateAsync(BuildKey("external-sharing", new[] { tid }));
        await InvalidateAsync(BuildKey("external-sharing", null));
    }

    // The SharePoint audit cursor is collection state, never cached.
    public Task<DateTime?> GetSharePointAuditCursorAsync(string tenantId)
        => _inner.GetSharePointAuditCursorAsync(tenantId);

    public Task UpdateSharePointAuditCursorAsync(string tenantId, DateTime cursorUtc)
        => _inner.UpdateSharePointAuditCursorAsync(tenantId, cursorUtc);

    // --- Privileged role inventory (60 min — daily-changing) ---
    public Task<List<PrivilegedRoleSnapshot>> GetPrivilegedRolesAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("privileged-roles", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetPrivilegedRolesAsync(tenantIds), options);
    }

    public async Task SavePrivilegedRolesAsync(string tenantId, DateTime reportDate, IEnumerable<PrivilegedRoleSnapshot> snapshots)
    {
        await _inner.SavePrivilegedRolesAsync(tenantId, reportDate, snapshots);
        await InvalidateAsync(BuildKey("privileged-roles", new[] { tenantId }), BuildKey("privileged-roles", null));
    }

    // --- Defender / M365 security alerts (60 min — daily-changing) ---
    public Task<List<DefenderAlertSnapshot>> GetDefenderAlertsAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("defender-alerts", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetDefenderAlertsAsync(tenantIds), options);
    }

    public async Task SaveDefenderAlertsAsync(string tenantId, DateTime reportDate, IEnumerable<DefenderAlertSnapshot> snapshots)
    {
        await _inner.SaveDefenderAlertsAsync(tenantId, reportDate, snapshots);
        await InvalidateAsync(BuildKey("defender-alerts", new[] { tenantId }), BuildKey("defender-alerts", null));
    }

    // --- Suspicious mailbox-rule / auto-forwarding audit (60 min — daily-changing) ---
    public Task<List<MailRuleEventSnapshot>> GetMailRuleEventsAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        var key = BuildKey("mail-rules", tenantIds, days);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetMailRuleEventsAsync(tenantIds, days), options);
    }

    public async Task SaveMailRuleEventsAsync(IEnumerable<MailRuleEventSnapshot> snapshots)
    {
        await _inner.SaveMailRuleEventsAsync(snapshots);
        foreach (var tid in snapshots.Select(s => s.TenantId).Distinct())
            await InvalidateAsync(BuildKey("mail-rules", new[] { tid }));
        await InvalidateAsync(BuildKey("mail-rules", null));
    }

    // The Exchange audit cursor is collection state, never cached.
    public Task<DateTime?> GetExchangeAuditCursorAsync(string tenantId)
        => _inner.GetExchangeAuditCursorAsync(tenantId);

    public Task UpdateExchangeAuditCursorAsync(string tenantId, DateTime cursorUtc)
        => _inner.UpdateExchangeAuditCursorAsync(tenantId, cursorUtc);

    // --- DLP policy matches (60 min — daily-changing) ---
    public Task<List<DlpEventSnapshot>> GetDlpEventsAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        var key = BuildKey("dlp-events", tenantIds, days);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetDlpEventsAsync(tenantIds, days), options);
    }

    public async Task SaveDlpEventsAsync(IEnumerable<DlpEventSnapshot> snapshots)
    {
        await _inner.SaveDlpEventsAsync(snapshots);
        foreach (var tid in snapshots.Select(s => s.TenantId).Distinct())
            await InvalidateAsync(BuildKey("dlp-events", new[] { tid }));
        await InvalidateAsync(BuildKey("dlp-events", null));
    }

    // The DLP audit cursor is collection state, never cached.
    public Task<DateTime?> GetDlpAuditCursorAsync(string tenantId)
        => _inner.GetDlpAuditCursorAsync(tenantId);

    public Task UpdateDlpAuditCursorAsync(string tenantId, DateTime cursorUtc)
        => _inner.UpdateDlpAuditCursorAsync(tenantId, cursorUtc);

    // --- Directory subscriptions / license renewals (60 min — daily-changing) ---
    public Task<List<SubscriptionSnapshot>> GetSubscriptionsAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("subscriptions", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetSubscriptionsAsync(tenantIds), options);
    }

    public async Task SaveSubscriptionsAsync(string tenantId, DateTime reportDate, IEnumerable<SubscriptionSnapshot> snapshots)
    {
        await _inner.SaveSubscriptionsAsync(tenantId, reportDate, snapshots);
        await InvalidateAsync(BuildKey("subscriptions", new[] { tenantId }), BuildKey("subscriptions", null));
    }

    // --- Teams team & channel activity (60 min — daily-changing) ---
    public Task<List<TeamsTeamActivitySnapshot>> GetTeamsTeamActivityAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("teams-team-activity", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetTeamsTeamActivityAsync(tenantIds), options);
    }

    public async Task SaveTeamsTeamActivityAsync(string tenantId, DateTime reportDate, IEnumerable<TeamsTeamActivitySnapshot> snapshots)
    {
        await _inner.SaveTeamsTeamActivityAsync(tenantId, reportDate, snapshots);
        await InvalidateAsync(BuildKey("teams-team-activity", new[] { tenantId }), BuildKey("teams-team-activity", null));
    }

    // --- Directory audit / change tracking (60 min — daily-changing) ---
    public Task<List<DirectoryAuditSnapshot>> GetDirectoryAuditsAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        var key = BuildKey("directory-audit", tenantIds, days);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetDirectoryAuditsAsync(tenantIds, days), options);
    }

    public async Task SaveDirectoryAuditsAsync(IEnumerable<DirectoryAuditSnapshot> snapshots)
    {
        await _inner.SaveDirectoryAuditsAsync(snapshots);
        foreach (var tid in snapshots.Select(s => s.TenantId).Distinct())
            await InvalidateAsync(BuildKey("directory-audit", new[] { tid }));
        await InvalidateAsync(BuildKey("directory-audit", null));
    }

    // The directory-audit cursor is collection state, never cached.
    public Task<DateTime?> GetDirectoryAuditCursorAsync(string tenantId)
        => _inner.GetDirectoryAuditCursorAsync(tenantId);

    public Task UpdateDirectoryAuditCursorAsync(string tenantId, DateTime cursorUtc)
        => _inner.UpdateDirectoryAuditCursorAsync(tenantId, cursorUtc);

    // --- License assignment errors & seat waste (60 min — daily-changing) ---
    public Task<List<LicenseAssignmentIssueSnapshot>> GetLicenseAssignmentIssuesAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("license-issues", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetLicenseAssignmentIssuesAsync(tenantIds), options);
    }

    public async Task SaveLicenseAssignmentIssuesAsync(string tenantId, DateTime reportDate, IEnumerable<LicenseAssignmentIssueSnapshot> snapshots)
    {
        await _inner.SaveLicenseAssignmentIssuesAsync(tenantId, reportDate, snapshots);
        await InvalidateAsync(BuildKey("license-issues", new[] { tenantId }), BuildKey("license-issues", null));
    }

    // --- OAuth app consent grants (60 min — daily-changing) ---
    public Task<List<OAuthGrantSnapshot>> GetOAuthGrantsAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("oauth-grants", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetOAuthGrantsAsync(tenantIds), options);
    }

    public async Task SaveOAuthGrantsAsync(string tenantId, DateTime reportDate, IEnumerable<OAuthGrantSnapshot> snapshots)
    {
        await _inner.SaveOAuthGrantsAsync(tenantId, reportDate, snapshots);
        await InvalidateAsync(BuildKey("oauth-grants", new[] { tenantId }), BuildKey("oauth-grants", null));
    }

    // --- Mailbox non-owner / delegate access (60 min — daily-changing) ---
    public Task<List<MailboxAccessSnapshot>> GetMailboxAccessAsync(IEnumerable<string>? tenantIds, int days = 30)
    {
        var key = BuildKey("mailbox-access", tenantIds, days);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetMailboxAccessAsync(tenantIds, days), options);
    }

    public async Task SaveMailboxAccessAsync(IEnumerable<MailboxAccessSnapshot> snapshots)
    {
        await _inner.SaveMailboxAccessAsync(snapshots);
        foreach (var tid in snapshots.Select(s => s.TenantId).Distinct())
            await InvalidateAsync(BuildKey("mailbox-access", new[] { tid }));
        await InvalidateAsync(BuildKey("mailbox-access", null));
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
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions15Min;
        return GetOrSetAsync(key, () => _inner.GetMauHistoryAsync(tenantIds, months), options);
    }
    public Task<List<MauSnapshot>> GetLatestMauByServiceAsync(string? tenantId = null)
    {
        var key = BuildKey("mau-latest", tenantId != null ? new[] { tenantId } : null);
        return GetOrSetAsync(key, () => _inner.GetLatestMauByServiceAsync(tenantId), CacheOptions15Min);
    }
    public Task<List<MauSnapshot>> GetLatestMauByServiceAsync(IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("mau-latest", tenantIds);
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions15Min;
        return GetOrSetAsync(key, () => _inner.GetLatestMauByServiceAsync(tenantIds), options);
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
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetLatestLicensesAsync(tenantIds), options);
    }
    public Task<List<LicenseSnapshot>> GetLicensesByDateRangeAsync(DateTime from, DateTime to, string? tenantId = null)
    {
        var key = BuildKey("licenses-range", tenantId != null ? new[] { tenantId } : null, from.ToString("yyyyMMdd"), to.ToString("yyyyMMdd"));
        return GetOrSetAsync(key, () => _inner.GetLicensesByDateRangeAsync(from, to, tenantId), CacheOptions60Min);
    }
    public Task<List<LicenseSnapshot>> GetLicensesByDateRangeAsync(DateTime from, DateTime to, IEnumerable<string>? tenantIds)
    {
        var key = BuildKey("licenses-range", tenantIds, from.ToString("yyyyMMdd"), to.ToString("yyyyMMdd"));
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetLicensesByDateRangeAsync(from, to, tenantIds), options);
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
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetMessageCenterPostsAsync(tenantIds), options);
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
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions15Min;
        return GetOrSetAsync(key, () => _inner.GetSecuritySummaryAsync(tenantIds, days), options);
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
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions15Min;
        return GetOrSetAsync(key, () => _inner.GetWorkloadActivityAsync(tenantIds, days), options);
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
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetCopilotUsageAsync(tenantIds), options);
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
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetUserSegmentsAsync(tenantIds, months), options);
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
        var options = IsMultiTenantCombo(tenantIds) ? CacheOptionsShort : CacheOptions60Min;
        return GetOrSetAsync(key, () => _inner.GetDepartmentUsageAsync(tenantIds), options);
    }
    public async Task SaveDepartmentUsageAsync(IEnumerable<DepartmentUsageSnapshot> snapshots)
    {
        await _inner.SaveDepartmentUsageAsync(snapshots);
        await InvalidateAsync(BuildKey("departments", null));
    }
}
