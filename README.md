# Modern Workplace Dashboard (MWDashboard)

A Blazor Web App that visualizes Monthly Active Users (MAU) across Microsoft 365 and Modern Workplace services, pulling data from the Microsoft Graph Reports API. Includes license adoption analytics, feature-level activity tracking, Copilot adoption monitoring, user segmentation, department-level adoption, security sign-in monitoring, Microsoft Secure Score, MFA/auth-method registration, inactive/stale account analysis, M365 service health, identity & device governance (Intune device compliance, Conditional Access coverage, guest users, risky users), and M365 Message Center integration. Multi-tenant aware with per-tenant consent-health monitoring and Entra ID tier detection.

## Tech Stack

- **.NET 10** Blazor Web App (Server interactivity)
- **MudBlazor** — UI component library
- **Blazor-ApexCharts** — Interactive charts
- **Microsoft Graph SDK** — Usage reports & license data
- **Microsoft Graph Beta SDK** — Sign-in logs with full authentication details (Entra ID P1/P2)
- **EF Core + Azure SQL** — Data persistence (Serverless tier with auto-pause)
- **Azure Managed Redis** — Distributed caching for dashboard performance
- **Azure Container Apps** — Hosting: web UI, on-demand collector (scales 0→N), and scheduled jobs (data collection)
- **Azure Key Vault** — Secrets management (AD credentials, Redis connection string)
- **Application Insights** — Distributed tracing, metrics, and logging via OpenTelemetry
- **Managed Identities** — Passwordless auth to SQL, ACR, and Key Vault
- **Azure Identity** — Multi-tenant authentication
- **Azure Developer CLI (azd)** — Infrastructure provisioning & deployment (.NET SDK container publish, no Docker required)

## Project Structure

```
MWDashboard/
├── azure.yaml                          # azd project definition
├── MWDashboard.slnx                    # Solution file
├── .github/workflows/deploy.yml        # CI/CD pipeline (GitHub Actions)
├── infra/                              # Bicep infrastructure-as-code
│   ├── main.bicep                      # Orchestrator
│   ├── main.bicepparam                 # Parameters (reads env vars)
│   └── modules/                        # Individual resource modules
└── src/
    ├── MWDashboard.Shared/             # Shared library (Models, Data, Services)
    ├── MWDashboard.Web/                # Blazor UI → Azure Container App
    ├── MWDashboard.Collector/          # On-demand collection API → Azure Container App (internal)
    └── MWDashboard.Job/                # Scheduled data collector → Azure Container App Job
```

## Quick Start (Local Development)

### Prerequisites

- .NET 10 SDK
- An Azure AD multi-tenant app registration (see [Permissions](docs/permissions.md))
- SQL Server (local) or SQLite for development

### Configuration

1. Update `src/MWDashboard.Web/appsettings.Development.json` with your `ClientId` and `TenantId`
2. Store the client secret securely:
   ```powershell
   cd src/MWDashboard.Web
   dotnet user-secrets set "AzureAd:ClientSecret" "your-secret"
   ```

### Run

```powershell
dotnet run --project src/MWDashboard.Web
```

The database is auto-migrated on first run (Development mode).

### Onboarding a Tenant

1. Navigate to `/tenants`
2. Generate an admin consent URL and send it to the customer's Global Admin
3. After consent is granted, register the tenant ID and name
4. Click the **Collect Now** button (cloud sync icon) to immediately pull data
5. The scheduled job collects data automatically on a daily 2:00 AM UTC cycle

## Azure Deployment

See [Deployment Guide](docs/deployment.md) for full instructions.

**Quick deploy with azd:**
```powershell
azd auth login
azd env new prod
azd env set AZURE_AD_CLIENT_ID <your-client-id>
azd env set AZURE_AD_CLIENT_SECRET <your-secret>
azd env set AZURE_AD_TENANT_ID <your-tenant-id>
azd up
```

## Documentation

| Document | Description |
|----------|-------------|
| [Architecture](docs/architecture.md) | System diagrams, data flow, multi-tenant model, project structure |
| [Deployment](docs/deployment.md) | Azure deployment guide, infrastructure, CI/CD pipeline setup |
| [Features](docs/features.md) | Detailed feature documentation per page |
| [Permissions](docs/permissions.md) | Required API permissions, consent guide, troubleshooting |

## License

Proprietary — internal use only.
