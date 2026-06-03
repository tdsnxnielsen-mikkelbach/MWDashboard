using Microsoft.EntityFrameworkCore;
using MWDashboard.Data;
using MWDashboard.Models;

namespace MWDashboard.Services;

public class TenantFilterService
{
    private readonly IDbContextFactory<MauDbContext> _dbFactory;
    private List<TenantInfo> _allTenants = [];
    private HashSet<string> _selectedTenantIds = [];

    public event Func<Task>? OnChangeAsync;

    public TenantFilterService(IDbContextFactory<MauDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

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

        _allTenants = await db.Tenants.Where(t => t.IsActive).OrderBy(t => t.TenantName).ToListAsync();

        var currentActiveIds = _allTenants.Select(t => t.TenantId).ToHashSet();

        // Remove deactivated tenants from selection
        _selectedTenantIds.IntersectWith(currentActiveIds);

        // Auto-select newly activated tenants (tenants that weren't in the previous active set)
        var newlyActivated = currentActiveIds.Except(previousActiveIds);
        foreach (var id in newlyActivated)
            _selectedTenantIds.Add(id);

        // Default: select all if none selected
        if (_selectedTenantIds.Count == 0)
            _selectedTenantIds = currentActiveIds;

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

    /// <summary>
    /// Returns null if all tenants are selected (no filter needed), otherwise returns the selected IDs.
    /// Data service methods accept null as "all tenants".
    /// </summary>
    public IEnumerable<string>? GetFilteredTenantIds()
    {
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
