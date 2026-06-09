---
name: new-page
description: "Scaffold a new MWDashboard page. Use when creating a new Blazor dashboard page with MudBlazor, ApexCharts, TenantFilter integration, KPI cards, and data loading patterns."
argument-hint: "Page name and data source (e.g., 'Compliance page showing device compliance snapshots')"
---

# Scaffold a New Dashboard Page

## When to Use
- Adding a new visualization/dashboard page to the web app
- User asks to create a page, view, or dashboard for a new data source

## Procedure

### 1. Create the Razor component

File: `src/MWDashboard.Web/Components/Pages/{PageName}.razor`

Follow this structure exactly:

```razor
@page "/{route}"
@using ApexCharts
@using Color = MudBlazor.Color
@using Size = MudBlazor.Size
@inject IMauDataService MauDataService
@inject TenantFilterService TenantFilter
@implements IDisposable

<PageTitle>{Page Title}</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">{Page Title}</MudText>

@if (_loading)
{
    <MudProgressLinear Color="Color.Primary" Indeterminate="true" Class="mb-4" />
    <div class="d-flex flex-column align-center justify-center" style="min-height: 400px;">
        <MudProgressCircular Color="Color.Primary" Indeterminate="true" Size="Size.Large" />
        <MudText Typo="Typo.body1" Color="Color.Secondary" Class="mt-4">Loading data...</MudText>
    </div>
}
else
{
    @* KPI cards, charts, tables go here *@
}

@code {
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        TenantFilter.OnChangeAsync += OnTenantFilterChangedAsync;
        await LoadData();
    }

    private Task OnTenantFilterChangedAsync()
    {
        return InvokeAsync(async () =>
        {
            TenantFilter.SetLoading(true);
            _loading = true;
            StateHasChanged();
            await LoadData();
            TenantFilter.SetLoading(false);
            StateHasChanged();
        });
    }

    private async Task LoadData()
    {
        _loading = true;
        StateHasChanged();

        var tenantIds = TenantFilter.GetFilteredTenantIds();
        // Load data from MauDataService using tenantIds
        // ...

        _loading = false;
    }

    public void Dispose()
    {
        TenantFilter.OnChangeAsync -= OnTenantFilterChangedAsync;
    }
}
```

### 2. Required patterns

- **TenantFilter lifecycle**: Subscribe in `OnInitializedAsync`, unsubscribe in `Dispose()`
- **Loading state**: Set `TenantFilter.SetLoading(true/false)` around data loads
- **Tenant scoping**: Always pass `TenantFilter.GetFilteredTenantIds()` to data service methods
- **Multi-tenant labels**: Check `TenantFilter.IsMultiTenantView` for chart series naming (append tenant name)
- **Chart re-render**: Use `@key` on `<ApexChart>` components bound to changing data/filters
- **MudBlazor disambiguation**: Add `@using Color = MudBlazor.Color` and `@using Size = MudBlazor.Size`

### 3. KPI cards pattern

```razor
<MudGrid>
    <MudItem xs="12" sm="6" md="3">
        <MudPaper Class="pa-4 d-flex flex-column align-center" Elevation="2">
            <MudText Typo="Typo.caption" Color="Color.Secondary">Label</MudText>
            <MudText Typo="Typo.h4" Color="Color.Primary">@_value.ToString("N0")</MudText>
        </MudPaper>
    </MudItem>
</MudGrid>
```

### 4. ApexCharts pattern

```razor
<ApexChart TItem="ChartDataPoint" Options="_chartOptions" Height="350" @key="@_chartKey">
    <ApexPointSeries TItem="ChartDataPoint"
                     Items="_chartData"
                     Name="Series Name"
                     SeriesType="SeriesType.Line"
                     XValue="@(e => e.Date)"
                     YValue="@(e => (decimal?)e.Value)" />
</ApexChart>
```

### 5. Add navigation entry

Add a `<MudNavLink>` in `src/MWDashboard.Web/Components/Layout/NavMenu.razor`:

```razor
<MudNavLink Href="{route}" Icon="@Icons.Material.Filled.{Icon}">
    {Page Title}
</MudNavLink>
```

### 6. Checklist

- [ ] Page file created with correct `@page` route
- [ ] TenantFilter subscription + disposal wired
- [ ] Loading spinner shown during data fetch
- [ ] Data service method called with tenant IDs
- [ ] Multi-tenant chart labels handled
- [ ] NavMenu entry added
- [ ] `@key` set on charts that depend on filters
