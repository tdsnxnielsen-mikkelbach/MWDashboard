# MWDashboard Copilot Instructions

## Project Architecture

- **Solution**: Multi-project .NET 10 solution with `MWDashboard.Shared`, `MWDashboard.Web`, and `MWDashboard.Job` under `src/`
- **UI**: Blazor Server with MudBlazor + Blazor-ApexCharts
- **Data**: EF Core + Azure SQL Serverless (auto-pause), Redis distributed cache
- **Auth**: Azure AD multi-tenant, app-only (client credentials) per tenant
- **Hosting**: Azure Container Apps (web + scheduled job)
- **Infra**: Bicep IaC via Azure Developer CLI (azd)
- **No test projects** — manual/integration testing only

> **Note**: The root-level `Components/`, `Services/`, `Models/`, `Data/` folders are a legacy scaffold. The active source code lives under `src/MWDashboard.Shared/`, `src/MWDashboard.Web/`, and `src/MWDashboard.Job/`.

## Commands

```powershell
# Run locally
dotnet run --project src/MWDashboard.Web

# Build all projects
dotnet build src/MWDashboard.Web

# Add EF Core migration (run from src/MWDashboard.Web/)
dotnet ef migrations add <Name> --project ../MWDashboard.Shared

# Deploy to Azure (provision + deploy)
azd up

# Deploy code only (skip infra)
azd deploy
```

## Key Conventions

### Data Models (src/MWDashboard.Shared/Models/)
- All snapshot models follow: `Id`, `TenantId`, `TenantName`, `ReportDate`, metric fields, `CollectedAt`
- Composite unique indexes on `(TenantId, ServiceName/SkuId/Workload+ActivityType/AppName/Department, ReportDate)` for upsert deduplication
- Use `DateTime` for dates (UTC everywhere)
- 12 DbSets: MauSnapshots, Tenants, LicenseSnapshots, MessageCenterPosts, SecuritySignInSummaries, WorkloadActivities, CopilotUsageSnapshots, UserSegmentSnapshots, DepartmentUsageSnapshots, StorageSnapshots, ConsumptionSnapshots, M365AppUsageSnapshots

### Data Services (src/MWDashboard.Shared/Services/)
- Use `IDbContextFactory<MauDbContext>` — create a new context per method call (`await using var db = await _dbFactory.CreateDbContextAsync()`)
- All query methods accept `IEnumerable<string>? tenantIds` — `null` means "all tenants" (no WHERE clause = query optimization)
- Save methods use upsert logic: check existing by composite key, update if found, add if not
- Interfaces defined alongside implementations (e.g., `IMauDataService` + `MauDataService`)

### Graph API Services (src/MWDashboard.Web/Services/ or src/MWDashboard.Shared/Services/)
- `CreateClientForTenant(string tenantId)` creates a `GraphServiceClient` with `ClientSecretCredential`
- Report endpoints return CSV streams — parse with header matching
- Beta API uses separate `BetaGraphClient` instance (sign-ins + Copilot usage)
- Always handle `ServiceException` gracefully (tenant may not have required license)
- New endpoints: Teams/SharePoint/OneDrive/Exchange activity counts, Copilot user detail, Office365 active user detail (segmentation), /users (departments), SharePoint/OneDrive/Exchange storage usage, M365 App user counts

### Caching Strategy
- **Redis distributed cache** (`IDistributedCache`): `CachedMauDataService` decorator wraps `MauDataService` — all read methods cached
- **Cache key format**: `MWDashboard:{feature}:{tenantId|"all"}:{parameters}`
- **TTL 15 min**: MAU history/latest, workload activity, security, storage, consumption, M365 app usage (dashboard-level queries)
- **TTL 60 min**: Licenses, message center, Copilot, segmentation, departments, Entra tiers (daily-changing data)
- **Output caching**: Applied at HTTP level for full page responses (5 min base, 15 min dashboard)
- **Cache invalidation**: Every `Save*` method invalidates relevant cache keys automatically
- **Fallback**: System gracefully falls back to in-memory cache when Redis unavailable

### Page Patterns (src/MWDashboard.Web/Components/Pages/)
- Subscribe to `TenantFilter.OnChangeAsync` in `OnInitializedAsync`
- Set `TenantFilter.SetLoading(true/false)` around data loads
- Use `TenantFilter.GetFilteredTenantIds()` for tenant scoping
- Check `TenantFilter.IsMultiTenantView` for chart series labeling
- Dispose event subscription in `Dispose()`
- Use `@key` on chart components when date ranges change to force re-render

### Background Collection (src/MWDashboard.Job/)
- One-shot console app: collects all data for active tenants, then exits
- Runs as Azure Container App Job (cron: `0 2 * * *`)
- Same `IGraphReportService` / `IMauDataService` interfaces as web

### EF Core Migrations (src/MWDashboard.Shared/Migrations/)
- Add migrations from the Web project: `dotnet ef migrations add <Name> --project ../MWDashboard.Shared`
- Auto-migrate on startup in both Web and Job
- DbContext has 12 DbSets — all entities defined in `src/MWDashboard.Shared/Models/MauSnapshot.cs`

### DI Registration (src/MWDashboard.Web/Program.cs)
- `MauDataService` registered as Scoped (raw implementation)
- `IMauDataService` resolves to `CachedMauDataService` (decorator wrapping `MauDataService`)
- `IGraphReportService` → `GraphReportService` (Scoped)
- `TenantFilterService` → Scoped (shared state per circuit)
- `IDataCollectionService` → `OnDemandDataCollectionService` (Web) / inline in Job

### Blazor-ApexCharts Usage
- Import via `@using ApexCharts` in page components
- Use `<ApexChart>` with `ApexChartOptions<T>` for typed chart configuration
- Always set `@key` on chart components when data/date ranges change to force re-render
- Limit data points per series to avoid client performance issues

## Key Documentation

- [docs/architecture.md](../docs/architecture.md) — System diagrams, data flow, multi-tenant model
- [docs/deployment.md](../docs/deployment.md) — Azure provisioning, scaling, infrastructure details
- [docs/features.md](../docs/features.md) — All dashboard pages and their functionality
- [docs/permissions.md](../docs/permissions.md) — Required Graph API permissions per feature
- [docs/todo.md](../docs/todo.md) — Planned features and backlog

## Performance Guidelines

- **SQL Serverless cold-start**: EF Core configured with `EnableRetryOnFailure(5, 30s)` + 60s command timeout
- **Parallel builds**: `azure.yaml` has `prepackage` hook to pre-build shared project
- **Chart rendering**: Limit data points to avoid client-side performance issues (aggregate to daily/weekly/monthly as appropriate)
- **Large datasets**: Use `AsNoTracking()` for read-only queries, project to DTOs where possible
- **Redis TTL**: 15 min for dashboards, 60 min for license/historical data that changes daily
