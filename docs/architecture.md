# Architecture

## System Overview

```mermaid
graph TB
    subgraph Azure Container Apps
        Web[Container App: Web<br/>Blazor Dashboard UI]
        Job[Container App Job: Collector<br/>Scheduled daily 2AM UTC]
    end

    subgraph Shared Services
        SQL[(Azure SQL Serverless<br/>GP_S_Gen5)]
        Redis[(Azure Managed Redis<br/>Distributed Cache)]
        KV[Key Vault<br/>Secrets Store]
        UAMI[User-Assigned Managed Identity]
        ACR[Azure Container Registry]
        Logs[Log Analytics Workspace]
    end

    subgraph Microsoft Cloud
        Graph[Microsoft Graph API<br/>Reports.Read.All]
        GraphBeta[Microsoft Graph Beta API<br/>AuditLog.Read.All]
        AAD[Azure AD<br/>Multi-tenant App Registration]
        MsgCenter[M365 Message Center<br/>ServiceMessage.Read.All]
    end

    Web --> SQL
    Web --> Redis
    Job --> SQL
    Job --> Graph
    Job --> GraphBeta
    Job --> MsgCenter
    Web --> Graph
    Graph --> AAD
    GraphBeta --> AAD
    MsgCenter --> AAD
    ACR --> Web
    ACR --> Job
    Web --> Logs
    Job --> Logs
```

## Data Flow

```mermaid
sequenceDiagram
    participant Admin as Tenant Admin
    participant Web as Web Container App
    participant Job as Collector Job
    participant AAD as Azure AD
    participant Graph as Graph API
    participant DB as Azure SQL
    participant Cache as Redis Cache

    Note over Admin, Web: Onboarding
    Admin->>AAD: Grant admin consent
    Web->>DB: Register tenant

    Note over Job, DB: Scheduled Collection (Container App Job - daily 2AM UTC)
    Job->>DB: Get active tenants
    Job->>AAD: Authenticate (client credentials)
    AAD-->>Job: Access token
    Job->>Graph: GET /reports/getOffice365ActiveUserCounts(D180)
    Graph-->>Job: CSV report data
    Job->>Graph: GET /subscribedSkus
    Graph-->>Job: License data
    Job->>Graph: GET /admin/serviceAnnouncement/messages
    Graph-->>Job: Message Center posts
    Job->>GraphBeta: GET /auditLogs/signIns (Beta)
    GraphBeta-->>Job: Sign-in logs with AuthenticationDetails
    Job->>DB: Upsert MAU snapshots + licenses + posts + sign-ins

    Note over Admin, Web: On-demand Collection (via Web UI)
    Admin->>Web: Click "Collect Now" for tenant
    Web->>Graph: Fetch all data for tenant
    Web->>DB: Store results immediately

    Note over Web, Cache: Dashboard Display
    Web->>Cache: Check cached data
    Cache-->>Web: Cache hit (return cached)
    Web->>DB: Cache miss → query DB
    Web->>Cache: Store in cache (TTL 15min)
    Web->>Web: Render charts & KPIs
```

## Multi-Tenant Model

```mermaid
graph LR
    subgraph Your Tenant
        AppReg[App Registration<br/>Multi-tenant<br/>Reports.Read.All]
    end

    subgraph Customer Tenant A
        ConsentA[Admin Consent ✓]
    end

    subgraph Customer Tenant B
        ConsentB[Admin Consent ✓]
    end

    subgraph Customer Tenant C
        ConsentC[Admin Consent ✓]
    end

    AppReg --> ConsentA
    AppReg --> ConsentB
    AppReg --> ConsentC
```

## Project Structure

```
MWDashboard/
├── azure.yaml                              # azd project definition
├── MWDashboard.slnx                        # Solution file (3 projects)
├── .github/
│   └── workflows/
│       └── deploy.yml                      # GitHub Actions CI/CD pipeline
├── infra/                                  # Bicep infrastructure-as-code
│   ├── main.bicep                          # Resource orchestrator
│   ├── main.bicepparam                     # Parameters (env vars)
│   ├── abbreviations.json                  # Azure naming conventions
│   └── modules/
│       ├── container-registry.bicep        # Azure Container Registry (Basic)
│       ├── container-apps-environment.bicep # Container Apps Environment
│       ├── container-app-web.bicep         # Web UI Container App (ingress, scaling)
│       ├── container-app-job.bicep         # Scheduled job (cron: 0 2 * * *)
│       ├── key-vault.bicep                 # Key Vault (secrets for AD + Redis)
│       ├── log-analytics.bicep             # Log Analytics workspace
│       ├── managed-identity.bicep          # User-Assigned Managed Identity (SQL admin)
│       ├── redis.bicep                     # Azure Managed Redis (Balanced B0)
│       ├── role-assignment.bicep           # Reusable RBAC role assignment
│       └── sql-server.bicep               # Azure SQL Serverless (GP_S_Gen5_1)
├── src/
│   ├── MWDashboard.Shared/                # Shared class library
│   │   ├── Data/
│   │   │   └── MauDbContext.cs            # EF Core context — 5 DbSets
│   │   ├── Models/
│   │   │   ├── MauSnapshot.cs             # All entity models + TenantEntraTier
│   │   │   └── M365Services.cs            # Service name constants
│   │   └── Services/
│   │       ├── GraphReportService.cs      # Graph API integration (v1.0 + Beta)
│   │       ├── MauDataService.cs          # DB read/write operations
│   │       ├── TenantFilterService.cs     # Scoped state service for tenant selection
│   │       └── IDataCollectionService.cs  # Interface for on-demand collection
│   ├── MWDashboard.Web/                   # Blazor Web App → Container App
│   │   ├── Program.cs                     # Redis + output caching, EF Core (SQL Server)
│   │   ├── Services/
│   │   │   └── OnDemandDataCollectionService.cs  # Web-triggered data collection
│   │   ├── Components/
│   │   │   ├── Layout/
│   │   │   │   ├── MainLayout.razor       # MudBlazor shell
│   │   │   │   ├── TenantSelector.razor   # Global tenant filter
│   │   │   │   └── NavMenu.razor          # Navigation
│   │   │   └── Pages/
│   │   │       ├── Home.razor             # MAU dashboard with KPIs & charts
│   │   │       ├── Services.razor         # Per-service comparison
│   │   │       ├── Licenses.razor         # License adoption + Message Center
│   │   │       ├── Security.razor         # Security sign-in monitoring
│   │   │       └── Tenants.razor          # Tenant management
│   │   └── wwwroot/                       # Static assets
│   └── MWDashboard.Job/                   # Data collector → Container App Job
│       └── Program.cs                     # One-shot console app (collects & exits)
└── docs/
    ├── architecture.md                    # This file
    ├── deployment.md                      # Azure deployment & CI/CD guide
    ├── features.md                        # Feature documentation
    └── permissions.md                     # Permissions & consent guide
```

## Key Constraints

| Constraint | Mitigation |
|-----------|-----------|
| Graph reports max D180 (~6 months) | Scheduled job snapshots daily; history accumulates over time |
| Admin consent required per tenant | Built-in consent URL generator on Tenants page |
| Concealed usernames in some tenants | Dashboard uses aggregated counts only |
| Graph API throttling | Retry with exponential backoff (SDK built-in) |
| Azure SQL Serverless cold-start (~60s) | EF Core `EnableRetryOnFailure` (5 retries, 30s max delay) + 60s command timeout |
| Sign-in logs require Entra ID P1/P2 | Security page gracefully shows info alert if unavailable |
| Graph Beta SDK is preview | Used only for sign-in endpoint; stable API used elsewhere |
| Container App Job max 1hr runtime | Sufficient for hundreds of tenants; parallelism=1 ensures serialized collection |

## Caching Strategy

| Layer | Scope | TTL | Purpose |
|-------|-------|-----|---------|
| Output Cache | HTTP responses | 5–15 min | Avoids re-rendering identical dashboard pages |
| Redis Distributed Cache | Cross-instance | Configurable | Shared cache between scaled web replicas |
| In-Memory (fallback) | Single instance | Session lifetime | Local dev when Redis is unavailable |

## Scaling Model

- **Web Container App**: Scales 1–3 replicas based on HTTP concurrency (50 concurrent requests triggers scale-out)
- **Collector Job**: Runs daily at 2:00 AM UTC, scales to zero between runs, max 1 hour execution
- **Azure SQL Serverless**: Auto-pauses after 60 minutes idle, auto-resumes on first connection
- **Redis**: Balanced B0 tier (sufficient for dashboard caching patterns)
