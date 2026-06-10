using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public class TenantFilterService
{
    private readonly IDbContextFactory<MauDbContext> _dbFactory;
    private List<TenantInfo> _allTenants = [];
    private HashSet<string> _selectedTenantIds = [];
    private HashSet<string>? _scopedTenantIds; // null = unrestricted (home tenant user)

    public event Func<Task>? OnChangeAsync;

    public TenantFilterService(IDbContextFactory<MauDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Restrict this service to only show specific tenants (for customer-tenant users).
    /// Pass null for unrestricted access (home tenant users).
    /// </summary>
    public void SetTenantScope(IEnumerable<string>? tenantIds)
    {
        _scopedTenantIds = tenantIds?.ToHashSet();
    }

    public bool IsHomeTenantUser => _scopedTenantIds == null;

    public IReadOnlyList<TenantInfo> AllTenants => _allTenants;
    public IReadOnlySet<string> SelectedTenantIds => _selectedTenantIds;
    public int SelectedCount => _selectedTenantIds.Count;
    public bool IsAllSelected => _allTenants.Count > 0 && _selectedTenantIds.Count == _allTenants.Count;
    public bool IsMultiTenantView => _selectedTenantIds.Count > 1;
    public bool IsLoading { get; private set; }

    public async Task LoadTenantsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var previousActiveIds = _allTenants.Select(t => t.TenantId).ToHashSet();

        var query = db.Tenants.Where(t => t.IsActive);
        if (_scopedTenantIds != null)
            query = query.Where(t => _scopedTenantIds.Contains(t.TenantId));

        _allTenants = await query.OrderBy(t => t.TenantName).ToListAsync();

        var currentActiveIds = _allTenants.Select(t => t.TenantId).ToHashSet();

        _selectedTenantIds.IntersectWith(currentActiveIds);

        var newlyActivated = currentActiveIds.Except(previousActiveIds);
        foreach (var id in newlyActivated)
            _selectedTenantIds.Add(id);

        if (_selectedTenantIds.Count == 0)
            _selectedTenantIds = currentActiveIds;

        await NotifyChangedAsync();
    }

    public async Task SetSelectedAsync(IEnumerable<string> tenantIds)
    {
        _selectedTenantIds = tenantIds.ToHashSet();
        await NotifyChangedAsync();
    }

    public async Task ToggleTenantAsync(string tenantId)
    {
        if (!_selectedTenantIds.Remove(tenantId))
            _selectedTenantIds.Add(tenantId);
        await NotifyChangedAsync();
    }

    public async Task SelectAllAsync()
    {
        _selectedTenantIds = _allTenants.Select(t => t.TenantId).ToHashSet();
        await NotifyChangedAsync();
    }

    public async Task SelectNoneAsync()
    {
        _selectedTenantIds.Clear();
        await NotifyChangedAsync();
    }

    public void SetLoading(bool loading)
    {
        IsLoading = loading;
    }

    public IEnumerable<string>? GetFilteredTenantIds()
    {
        // Scoped users must always filter — never return null (which means "all tenants")
        if (_scopedTenantIds != null)
            return _selectedTenantIds.Count > 0 ? _selectedTenantIds : _scopedTenantIds;

        if (IsAllSelected || _selectedTenantIds.Count == 0)
            return null;
        return _selectedTenantIds;
    }

    public IEnumerable<string>? GetFilterIds()
    {
        // Scoped users must always filter — never return null (which means "all tenants")
        if (_scopedTenantIds != null)
            return _selectedTenantIds.Count > 0 ? _selectedTenantIds : _scopedTenantIds;

        if (IsAllSelected || _selectedTenantIds.Count == 0)
            return null;
        return _selectedTenantIds;
    }

    public string GetDisplayLabel()
    {
        if (IsAllSelected || _selectedTenantIds.Count == 0)
            return "All Tenants";
        if (_selectedTenantIds.Count == 1)
        {
            var tenant = _allTenants.FirstOrDefault(t => t.TenantId == _selectedTenantIds.First());
            return tenant?.DisplayName ?? tenant?.TenantName ?? "1 Tenant";
        }
        return $"{_selectedTenantIds.Count} Tenants";
    }

    public string GetTenantName(string tenantId)
    {
        var tenant = _allTenants.FirstOrDefault(t => t.TenantId == tenantId);
        return tenant?.DisplayName ?? tenant?.TenantName ?? tenantId[..8];
    }

    public async Task ClearSelectionAsync()
    {
        _selectedTenantIds.Clear();
        await NotifyChangedAsync();
    }

    private async Task NotifyChangedAsync()
    {
        var handler = OnChangeAsync;
        if (handler != null)
        {
            foreach (var d in handler.GetInvocationList().Cast<Func<Task>>())
            {
                try { await d(); }
                catch { /* subscriber may be disposed */ }
            }
        }
    }
}
