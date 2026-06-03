# Architecture

## System Overview

```mermaid
graph TB
    subgraph Blazor Web App
        UI[Dashboard UI<br/>MudBlazor + ApexCharts]
        Pages[Pages<br/>Home / Tenants / Services / Licenses / Security]
        Services[Application Services<br/>MauDataService / GraphReportService]
        BG[Background Service<br/>MauSnapshotBackgroundService<br/>+ On-demand Collection]
    end

    subgraph Data Layer
        DB[(SQLite Database<br/>MWDashboard.db)]
    end

    subgraph Microsoft Cloud
        Graph[Microsoft Graph API<br/>Reports.Read.All]
        GraphBeta[Microsoft Graph Beta API<br/>AuditLog.Read.All]
        AAD[Azure AD<br/>Multi-tenant App Registration]
        MsgCenter[M365 Message Center<br/>ServiceMessage.Read.All]
    end

    UI --> Pages
    Pages --> Services
    BG --> Services
    Services --> DB
    Services --> Graph
    Services --> GraphBeta
    Services --> MsgCenter
    Graph --> AAD
    GraphBeta --> AAD
    MsgCenter --> AAD
```

## Data Flow

```mermaid
sequenceDiagram
    participant Admin as Tenant Admin
    participant App as MWDashboard
    participant AAD as Azure AD
    participant Graph as Graph API
    participant DB as SQLite DB

    Note over Admin, App: Onboarding
    Admin->>AAD: Grant admin consent
    App->>DB: Register tenant

    Note over App, DB: Daily Collection (Background Service)
    loop Every 24 hours
        App->>DB: Get active tenants
        App->>AAD: Authenticate (client credentials)
        AAD-->>App: Access token
        App->>Graph: GET /reports/getOffice365ActiveUserCounts(D180)
        Graph-->>App: CSV report data
        App->>Graph: GET /subscribedSkus
        Graph-->>App: License data
        App->>Graph: GET /admin/serviceAnnouncement/messages
        Graph-->>App: Message Center posts
        App->>GraphBeta: GET /auditLogs/signIns (Beta)
        GraphBeta-->>App: Sign-in logs with AuthenticationDetails
        App->>DB: Upsert MAU snapshots + licenses + posts + sign-ins
    end

    Note over Admin, App: On-demand Collection
    Admin->>App: Click "Collect Now" for tenant
    App->>Graph: Fetch all data for tenant
    App->>DB: Store results immediately

    Note over App, DB: Dashboard Display
    App->>DB: Query MAU history (12 months)
    App->>App: Render charts & KPIs
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
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor        # MudBlazor shell (AppBar, Drawer, dark/light toggle)
│   │   ├── TenantSelector.razor    # Global tenant filter component
│   │   └── NavMenu.razor           # Navigation menu
│   ├── Pages/
│   │   ├── Home.razor              # MAU dashboard with KPIs & charts
│   │   ├── Services.razor          # Per-service sparklines & comparison chart
│   │   ├── Licenses.razor          # License adoption, date picker, recommendations, Message Center
│   │   ├── Security.razor          # Security sign-in monitoring (Entra, Defender, Intune)
│   │   └── Tenants.razor           # Tenant registration, consent URLs, collect now button
│   ├── App.razor
│   └── _Imports.razor
├── Data/
│   └── MauDbContext.cs             # EF Core context (SQLite) — 5 DbSets
├── Models/
│   ├── MauSnapshot.cs              # MauSnapshot, TenantInfo, LicenseSnapshot, MessageCenterPost, SecuritySignInSummary
│   └── M365Services.cs             # M365 + Security service name constants
├── Services/
│   ├── GraphReportService.cs       # Graph API integration (v1.0 + Beta)
│   ├── MauDataService.cs           # DB read/write operations (9 methods)
│   ├── TenantFilterService.cs      # Scoped state service for tenant selection
│   └── MauSnapshotBackgroundService.cs  # Scheduled + on-demand data collection
├── docs/
│   ├── architecture.md             # This file
│   ├── features.md                 # Detailed feature documentation
│   └── permissions.md              # Required permissions & consent guide
├── Program.cs
├── appsettings.json
└── MWDashboard.csproj
```

## Key Constraints

| Constraint | Mitigation |
|-----------|-----------|
| Graph reports max D180 (~6 months) | Background service snapshots monthly; history accumulates over time |
| Admin consent required per tenant | Built-in consent URL generator on Tenants page |
| Concealed usernames in some tenants | Dashboard uses aggregated counts only |
| Graph API throttling | Retry with exponential backoff (SDK built-in) |
| Sign-in logs require Entra ID P1/P2 | Security page gracefully shows info alert if unavailable |
| Graph Beta SDK is preview | Used only for sign-in endpoint; stable API used elsewhere |
