---
description: "Use when writing or modifying Bicep infrastructure files, Azure resource definitions, container app configuration, or deployment parameters."
applyTo: "infra/**"
---

# Infrastructure Conventions (Bicep IaC)

## Project Layout

- `infra/main.bicep` — Top-level orchestrator, wires modules together
- `infra/main.bicepparam` — Parameters file (reads `azd` environment variables via `readEnvironmentVariable()`)
- `infra/abbreviations.json` — Azure resource naming abbreviations
- `infra/modules/` — One file per resource type

## Naming Conventions

All resources use the format: `{abbreviation}-{projectName}-{environment}` (e.g., `ca-mwdashboard-prod`).
Abbreviations are defined in `infra/abbreviations.json`.

## Module Patterns

Each module:
- Accepts `location`, `tags`, and resource-specific params
- Outputs the resource `id` and `name` (plus connection strings where applicable)
- Uses `@description` decorators on all parameters
- Uses `existing` keyword to reference resources created in other modules

## Key Architecture Decisions

- **SQL**: Azure SQL Serverless GP_S_Gen5_1, auto-pause 60 min, Azure AD-only auth (managed identity as admin)
- **Redis**: Azure Managed Redis Balanced_B0, TLS 1.2, VolatileLRU eviction, OSSCluster access policy
- **Container Apps**: Environment with Log Analytics; web app has external ingress on port 8080 with 1–3 replicas
- **Job**: Container App Job, cron `0 2 * * *`, max 1 hour execution, 1 retry
- **Key Vault**: Stores `AzureAd--ClientSecret` and Redis connection string; container apps reference via `secretRef`
- **Identity**: Single User-Assigned Managed Identity shared across web, job, ACR pull, SQL admin, and Key Vault access
- **Registry**: Azure Container Registry Basic SKU, admin disabled, managed identity has `AcrPull`

## Parameter Handling

Parameters flow from `azd env` → `main.bicepparam` → `main.bicep` → modules:
```bicep
param clientId = readEnvironmentVariable('AZURE_AD_CLIENT_ID', '')
```

Never hard-code secrets in Bicep. Use Key Vault references or `azd env set` values.

## Adding a New Resource

1. Create `infra/modules/{resource-name}.bicep` with standard params (`location`, `tags`, etc.)
2. Wire it in `main.bicep` using a `module` declaration
3. Pass outputs (IDs, connection strings) to dependent modules
4. If the resource needs identity access, add a `role-assignment` module invocation

## Docs

- [docs/deployment.md](../docs/deployment.md) — Full deployment guide and scaling details
- [docs/architecture.md](../docs/architecture.md) — System diagrams and data flow
