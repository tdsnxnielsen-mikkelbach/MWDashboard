# Modern Workplace Dashboard (MWDashboard)

A Blazor Web App that visualizes Monthly Active Users (MAU) across Microsoft 365 and Modern Workplace services, pulling data from the Microsoft Graph Reports API. Includes license adoption analytics, security sign-in monitoring, and M365 Message Center integration.

## Tech Stack

- **.NET 10** Blazor Web App (Server interactivity)
- **MudBlazor** — UI component library
- **Blazor-ApexCharts** — Interactive charts
- **Microsoft Graph SDK** — Usage reports & license data
- **Microsoft Graph Beta SDK** — Sign-in logs with full authentication details (Entra ID P1/P2)
- **EF Core + SQLite** — Local data persistence & history accumulation
- **Azure Identity** — Multi-tenant authentication

## Quick Start

### Prerequisites

- .NET 10 SDK
- An Azure AD multi-tenant app registration (see [Permissions](docs/permissions.md))

### Configuration

1. Update `appsettings.json` with your `ClientId` and `TenantId`
2. Store the client secret securely:
   ```powershell
   dotnet user-secrets init
   dotnet user-secrets set "AzureAd:ClientSecret" "your-secret"
   ```

### Run

```powershell
cd MWDashboard
dotnet run
```

The SQLite database is auto-created on first run (Development mode).

### Onboarding a Tenant

1. Navigate to `/tenants`
2. Generate an admin consent URL and send it to the customer's Global Admin
3. After consent is granted, register the tenant ID and name
4. Click the **Collect Now** button (cloud sync icon) to immediately pull data
5. The background service will also collect data automatically on the next 24-hour cycle

## Documentation

| Document | Description |
|----------|-------------|
| [Architecture](docs/architecture.md) | System diagrams, data flow, multi-tenant model, project structure, constraints |
| [Features](docs/features.md) | Detailed feature documentation per page |
| [Permissions](docs/permissions.md) | Required API permissions, consent guide, troubleshooting |

## License

Proprietary — internal use only.
