# MWDashboard Copilot Instructions

## Project Architecture

- **Solution**: Multi-project .NET 10 solution with `MWDashboard.Shared`, `MWDashboard.Web`, `MWDashboard.Collector`, `MWDashboard.Consent`, and `MWDashboard.Job` under `src/`
- **UI**: Blazor Server with MudBlazor + Blazor-ApexCharts
- **Data**: EF Core + Azure SQL Serverless (auto-pause), Redis distributed cache
- **Auth**: Azure AD multi-tenant — app-only (client credentials) for Graph API, OpenID Connect for user access with tenant-scoped data isolation
- **Hosting**: Azure Container Apps (web + on-demand collector + consent callback + scheduled job) + Azure Static Web App (consent page)
- **Infra**: Bicep IaC via Azure Developer CLI (azd)
- **No test projects** — manual/integration testing only

> **Note**: The root-level `Components/`, `Services/`, `Models/`, `Data/` folders are a legacy scaffold. The active source code lives under `src/MWDashboard.Shared/`, `src/MWDashboard.Web/`, `src/MWDashboard.Collector/`, `src/MWDashboard.Consent/`, and `src/MWDashboard.Job/`.

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
- 19 DbSets: MauSnapshots, Tenants, LicenseSnapshots, MessageCenterPosts, SecuritySignInSummaries, WorkloadActivities, CopilotUsageSnapshots, UserSegmentSnapshots, DepartmentUsageSnapshots, StorageSnapshots, ConsumptionSnapshots, M365AppUsageSnapshots, SecureScoreSnapshots, SecureScoreControlSnapshots, MfaRegistrationSnapshots, InactiveAccountSnapshots, ServiceHealthSnapshots, ServiceHealthIssueSnapshots, BrandingSettings
- `BrandingSettings` is a singleton row: logo/favicon (Base64 + content type), 6 theme colors (light/dark × primary/secondary/appbar), app title
- `TenantInfo` tracks consent health: `MissingPermissions` (comma-separated Graph permissions that failed a consent probe; empty = all consented) + `PermissionsCheckedAt` (last probe time). Populated on every collection run
- `LicenseSnapshot.IncludedServices` stores comma-separated service names auto-detected from Graph API service plans (e.g. `"Exchange,Office365,OneDrive,SharePoint,Teams"`)

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
- License collection auto-detects included services from `sku.ServicePlans` via `M365Services.DetectServicesFromPlans()`
- New endpoints: Teams/SharePoint/OneDrive/Exchange activity counts, Copilot user detail, Office365 active user detail (segmentation), /users (departments), SharePoint/OneDrive/Exchange storage usage, M365 App user counts
- **Premium-tier gating**: `signInActivity`-based features (inactive accounts, sign-in summary) require Entra ID P1/P2. Both collection pipelines derive the tier via `TenantEntraTier.FromLicenses(...)` (from the already-collected SKUs, no extra Graph call) and skip these calls on free-tier tenants instead of issuing a guaranteed-403 request. `IsPremiumLicenseError(...)` distinguishes a premium-license 403 from a true permission 403 so logs/UI don't mislabel it
- **Consent health probe**: `CheckMissingPermissionsAsync(tenantId)` makes a minimal call per required permission (using non-premium endpoints) and returns the display names of permissions that fail with a genuine consent error (403 / "Invalid permission" / "S2SUnauthorized" / "Authorization_RequestDenied"); premium-license 403s are excluded. Results persist to `TenantInfo` via `UpdateTenantPermissionStatusAsync(...)` at the end of every collection run

### Caching Strategy
- **Redis distributed cache** (`IDistributedCache`): `CachedMauDataService` decorator wraps `MauDataService` — all read methods cached
- **Cache key format**: `MWDashboard:{feature}:{tenantId|"all"}:{parameters}`
- **TTL 15 min** (with 5 min sliding): MAU history/latest, workload activity, security, storage, consumption, M365 app usage (dashboard-level queries)
- **TTL 60 min** (with 20 min sliding): Licenses, message center, Copilot, segmentation, departments, Entra tiers (daily-changing data)
- **TTL 4 min** (absolute only): Multi-tenant combo queries (2+ specific tenants selected)
- **Output caching**: Applied at HTTP level for full page responses (5 min base, 15 min dashboard)
- **Cache invalidation**: Every `Save*` method invalidates relevant cache keys automatically + publishes via Redis pub/sub to all replicas
- **Cross-replica invalidation**: `RedisCacheInvalidationService` uses Redis pub/sub channel `MWDashboard:cache-invalidation`
- **Cache warm-up**: `CacheWarmupService` pre-populates common all-tenant queries on startup (avoids thundering herd on cold start)
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

### On-Demand Collector (src/MWDashboard.Collector/)
- Minimal API with single endpoint: `POST /collect/{tenantId}?tenantName=...`
- Scales 0→3 independently via Container Apps HTTP scaling (5 concurrent requests)
- Internal ingress only — not externally accessible
- Web app calls it via `HttpCollectorClient`; falls back to local collection if unreachable
- Shares `OnDemandDataCollectionService` from `MWDashboard.Shared`

### Consent Callback (src/MWDashboard.Consent/)
- Minimal API with single endpoint: `POST /consent-callback?tenant={tenantId}&token={hmac}`
- External ingress with CORS restricted to Static Web App origin
- Scales 0→2, lightweight (0.25 vCPU, 0.5 GB)
- Validates HMAC token (shared secret from Key Vault) to prevent unauthorized calls
- Calls Graph API `GET /organization` to verify consent and fetch tenant domain/display name
- Auto-registers tenant in DB (upserts `TenantInfo` with `IsActive = true`)
- Triggers initial data collection after registration
- Static Web App (`static/consent-complete/`) serves the consent redirect landing page (completely isolated from dashboard)

### Consent Static Page (static/consent-complete/)
- Azure Static Web App (Free tier) — customer-facing consent redirect landing page
- Parses Azure AD redirect params (`?tenant=`, `&admin_consent=True`)
- Computes HMAC token client-side and POSTs to Consent Callback container
- Shows branded success/error messages — no access to customer data
- Application Insights JS SDK for client-side telemetry
- Deploy-time placeholder injection: `%%CONSENT_CALLBACK_URL%%`, `%%CONSENT_SHARED_SECRET%%`, `%%APPINSIGHTS_CONNECTION_STRING%%`, `%%DASHBOARD_URL%%` (replaced via azd postdeploy hook)

### EF Core Migrations (src/MWDashboard.Shared/Migrations/)
- Add migrations from the Web project: `dotnet ef migrations add <Name> --project ../MWDashboard.Shared`
- Auto-migrate on startup in both Web and Job
- DbContext has 19 DbSets — all entities defined in `src/MWDashboard.Shared/Models/MauSnapshot.cs`

### Authentication & Authorization (src/MWDashboard.Web/)
- **OpenID Connect** via `Microsoft.Identity.Web` — multi-tenant (`TenantId: "common"`), authorization code flow (`ResponseType: "code"`)
- **ClientSecret reuse**: `AzureAdAuth:ClientSecret` copied from `AzureAd:ClientSecret` at startup (single secret for both Graph API and user auth)
- **Token validation**: `OnTokenValidated` event allows home tenant unconditionally; other tenants validated against DB (`Tenants.IsActive`)
- **Access control**: Removing/deactivating a tenant immediately blocks login for users from that tenant; adding/activating allows login
- **Access denied**: `OnRemoteFailure` redirects rejected tenants to `/access-denied` (anonymous endpoint) with user-friendly message (internal OIDC errors never exposed)
- **Data scoping**: `TenantFilterService.SetTenantScope()` called in `MainLayout` — home tenant users see all data; customer tenant users are restricted to their own tenant
- **Data isolation enforcement**: `GetFilteredTenantIds()` never returns `null` for scoped users — always passes tenant ID filter to queries
- **UI**: Home tenant users see the full `TenantSelector` + Tenants page; customer tenant users see only their tenant name (no selector) and cannot access `/tenants`
- **Route protection**: `.RequireAuthorization()` on `MapRazorComponents` enforces auth at HTTP level (triggers OIDC challenge before Blazor renders)
- **Forwarded headers**: `UseForwardedHeaders` with cleared `KnownIPNetworks`/`KnownProxies` trusts Container Apps proxy (TLS terminated at ingress, ensures `https://` in redirect URIs)
- **Redirect URI**: Must register `https://<web-url>/signin-oidc` in app registration
- **Config section**: `AzureAdAuth` (Instance, TenantId=common, ClientId, CallbackPath, SignedOutCallbackPath)

### DI Registration (src/MWDashboard.Web/Program.cs)
- `MauDataService` registered as Scoped (raw implementation)
- `IMauDataService` resolves to `CachedMauDataService` (decorator wrapping `MauDataService` + `RedisCacheInvalidationService`)
- `IGraphReportService` → `GraphReportService` (Scoped)
- `TenantFilterService` → Scoped (shared state per circuit, supports `SetTenantScope` for data isolation)
- `IDataCollectionService` → `HttpCollectorClient` (typed HttpClient, calls Collector container) / fallback to `OnDemandDataCollectionService` if no `CollectorBaseUrl` configured
- `RedisCacheInvalidationService` → Singleton (Redis pub/sub for cross-replica invalidation)
- `CacheWarmupService` → Hosted service (pre-populates cache on startup)
- `IConnectionMultiplexer` → Singleton (Redis connection for pub/sub, if Redis configured)
- `AddMicrosoftIdentityWebAppAuthentication` → OpenID Connect with tenant validation
- `AddCascadingAuthenticationState` → Blazor auth state cascaded to all components

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

- **SQL Serverless cold-start**: EF Core configured with `EnableRetryOnFailure(5, 30s, errorNumbers: [-2, 4060])` + 120s command timeout
- **Parallel builds**: `azure.yaml` has `prepackage` hook to pre-build shared project
- **Chart rendering**: Limit data points to avoid client-side performance issues (aggregate to daily/weekly/monthly as appropriate)
- **Large datasets**: Use `AsNoTracking()` for read-only queries, project to DTOs where possible
- **Redis TTL**: 15 min for dashboards, 60 min for license/historical data that changes daily
- **Cache warm-up**: `CacheWarmupService` runs on startup to avoid thundering herd after cold starts
- **Sliding expiration**: Active dashboards benefit from sliding within absolute cap (5/15 or 20/60 min)
- **Multi-tenant combos**: Cached with short 4-min absolute TTL (avoids SQL hits for filtered views)
- **Cross-replica invalidation**: Redis pub/sub ensures all Web replicas drop stale keys simultaneously
- **Collector isolation**: On-demand collection offloaded to separate container (scales independently, doesn't block Web UI)

## Observability (OpenTelemetry)

- **SDK**: `Azure.Monitor.OpenTelemetry.AspNetCore` v1.5.0 (in `MWDashboard.Shared`, used by all 4 services)
- **Setup pattern**: Conditional on `APPLICATIONINSIGHTS_CONNECTION_STRING` env var — no telemetry when absent (local dev without AI)
- **Wiring** (all services): `builder.Services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = aiConnectionString);`
- **Auto-instrumented**: HTTP requests (in/out), SQL queries, Redis commands, ILogger correlation
- **Infrastructure**: `infra/modules/application-insights.bicep` → linked to Log Analytics workspace
- **Env var injection**: Bicep passes `APPLICATIONINSIGHTS_CONNECTION_STRING` to all 4 container apps/job + Static Web App (via predeploy hook)
- **End-to-end tracing**: Web → Collector requests propagate W3C TraceContext (correlate full collection flow in Transaction Search)
- **Client-side telemetry**: Static Web App consent page uses Application Insights JS SDK for page views and consent success/failure tracking
