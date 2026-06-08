# MWDashboard Copilot Instructions

## Project Architecture

- **Solution**: Multi-project .NET 10 solution with `MWDashboard.Shared`, `MWDashboard.Web`, and `MWDashboard.Job`
- **UI**: Blazor Server with MudBlazor + Blazor-ApexCharts
- **Data**: EF Core + Azure SQL Serverless (auto-pause), Redis distributed cache
- **Auth**: Azure AD multi-tenant, app-only (client credentials) per tenant
- **Hosting**: Azure Container Apps (web + scheduled job)
- **Infra**: Bicep IaC via Azure Developer CLI (azd)

## Key Conventions

### Data Models (src/MWDashboard.Shared/Models/)
- All snapshot models follow: `Id`, `TenantId`, `TenantName`, `ReportDate`, metric fields, `CollectedAt`
- Composite unique indexes on `(TenantId, ServiceName/SkuId/Workload+ActivityType/AppName/Department, ReportDate)` for upsert deduplication
- Use `DateTime` for dates (UTC everywhere)
- 11 DbSets: MauSnapshots, Tenants, LicenseSnapshots, MessageCenterPosts, SecuritySignInSummaries, WorkloadActivities, CopilotUsageSnapshots, UserSegmentSnapshots, DepartmentUsageSnapshots, StorageSnapshots, ConsumptionSnapshots

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
- New endpoints: Teams/SharePoint/OneDrive/Exchange activity counts, Copilot user detail, Office365 active user detail (segmentation), /users (departments), SharePoint/OneDrive/Exchange storage usage

### Caching Strategy
- **Redis distributed cache** (`IDistributedCache`): Use for expensive queries with TTL 15–60 minutes
- **Cache key format**: `MWDashboard:{feature}:{tenantId|"all"}:{parameters}`
- **Output caching**: Applied at HTTP level for full page responses (5 min base, 15 min dashboard)
- **Cache invalidation**: Invalidate on data collection (after saving new snapshots)
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

## Performance Guidelines

- **SQL Serverless cold-start**: EF Core configured with `EnableRetryOnFailure(5, 30s)` + 60s command timeout
- **Parallel builds**: `azure.yaml` has `prepackage` hook to pre-build shared project
- **Chart rendering**: Limit data points to avoid client-side performance issues (aggregate to daily/weekly/monthly as appropriate)
- **Large datasets**: Use `AsNoTracking()` for read-only queries, project to DTOs where possible
- **Redis TTL**: 15 min for dashboards, 60 min for license/historical data that changes daily
