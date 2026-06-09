targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment (e.g., dev, staging, prod)')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string

@description('Azure AD Client ID for Microsoft Graph access')
@secure()
param azureAdClientId string

@description('Azure AD Client Secret for Microsoft Graph access')
@secure()
param azureAdClientSecret string

@description('Azure AD Tenant ID for the app registration')
param azureAdTenantId string

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = {
  'azd-env-name': environmentName
}

// Resource Group
resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: '${abbrs.resourcesResourceGroups}${environmentName}'
  location: location
  tags: tags
}

// Container Registry
module acr './modules/container-registry.bicep' = {
  name: 'container-registry'
  scope: rg
  params: {
    name: '${abbrs.containerRegistryRegistries}${resourceToken}'
    location: location
    tags: tags
  }
}

// User-Assigned Managed Identity (shared by container apps for SQL access)
module identity './modules/managed-identity.bicep' = {
  name: 'managed-identity'
  scope: rg
  params: {
    name: 'id-${resourceToken}'
    location: location
    tags: tags
  }
}

// Log Analytics Workspace
module logAnalytics './modules/log-analytics.bicep' = {
  name: 'log-analytics'
  scope: rg
  params: {
    name: '${abbrs.operationalInsightsWorkspaces}${resourceToken}'
    location: location
    tags: tags
  }
}

// Azure SQL Server and Database
module sql './modules/sql-server.bicep' = {
  name: 'sql-server'
  scope: rg
  params: {
    name: '${abbrs.sqlServers}${resourceToken}'
    location: location
    tags: tags
    databaseName: 'MWDashboard'
    adminObjectId: identity.outputs.principalId
    adminLoginName: identity.outputs.name
    managedIdentityClientId: identity.outputs.clientId
  }
}

// Azure Cache for Redis
module redis './modules/redis.bicep' = {
  name: 'redis'
  scope: rg
  params: {
    name: '${abbrs.cacheRedis}${resourceToken}'
    location: location
    tags: tags
  }
}

// Container Apps Environment
module containerEnv './modules/container-apps-environment.bicep' = {
  name: 'container-apps-environment'
  scope: rg
  params: {
    name: '${abbrs.appManagedEnvironments}${resourceToken}'
    location: location
    tags: tags
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

// Key Vault for secrets
module keyVault './modules/key-vault.bicep' = {
  name: 'key-vault'
  scope: rg
  params: {
    name: 'kv-${resourceToken}'
    location: location
    tags: tags
    azureAdClientId: azureAdClientId
    azureAdClientSecret: azureAdClientSecret
    redisConnectionString: redis.outputs.connectionString
  }
}

// Pre-assign AcrPull to UAMI so container apps can pull images on first deploy
module acrPullUami './modules/role-assignment.bicep' = {
  name: 'acr-pull-uami'
  scope: rg
  params: {
    principalId: identity.outputs.principalId
    roleDefinitionId: '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull
    principalType: 'ServicePrincipal'
  }
}

// Pre-assign Key Vault Secrets User to UAMI so container apps can read secrets on first deploy
module kvSecretsUami './modules/role-assignment.bicep' = {
  name: 'kv-secrets-uami'
  scope: rg
  params: {
    principalId: identity.outputs.principalId
    roleDefinitionId: '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
    principalType: 'ServicePrincipal'
  }
}

// Web Container App (Blazor Dashboard)
module web './modules/container-app-web.bicep' = {
  name: 'container-app-web'
  scope: rg
  params: {
    name: '${abbrs.appContainerApps}web-${resourceToken}'
    location: location
    tags: tags
    containerAppsEnvironmentId: containerEnv.outputs.id
    containerRegistryLoginServer: acr.outputs.loginServer
    imageName: ''
    sqlConnectionString: sql.outputs.connectionString
    keyVaultUri: keyVault.outputs.uri
    azureAdTenantId: azureAdTenantId
    managedIdentityId: identity.outputs.id
    collectorFqdn: ondemand.outputs.fqdn
  }
}

// Job Container App (Data Collection)
module job './modules/container-app-job.bicep' = {
  name: 'container-app-job'
  scope: rg
  params: {
    name: '${abbrs.appContainerApps}job-${resourceToken}'
    location: location
    tags: tags
    containerAppsEnvironmentId: containerEnv.outputs.id
    containerRegistryLoginServer: acr.outputs.loginServer
    imageName: ''
    sqlConnectionString: sql.outputs.connectionString
    keyVaultUri: keyVault.outputs.uri
    azureAdTenantId: azureAdTenantId
    managedIdentityId: identity.outputs.id
  }
}

// On-Demand Collector Container App (HTTP API for data collection)
module ondemand './modules/container-app-collector.bicep' = {
  name: 'container-app-collector'
  scope: rg
  dependsOn: [acrPullUami, kvSecretsUami]
  params: {
    name: '${abbrs.appContainerApps}collect-${resourceToken}'
    location: location
    tags: tags
    containerAppsEnvironmentId: containerEnv.outputs.id
    containerRegistryLoginServer: acr.outputs.loginServer
    imageName: ''
    sqlConnectionString: sql.outputs.connectionString
    keyVaultUri: keyVault.outputs.uri
    azureAdTenantId: azureAdTenantId
    managedIdentityId: identity.outputs.id
  }
}

// Role assignments - AcrPull for container apps
module acrPullWeb './modules/role-assignment.bicep' = {
  name: 'acr-pull-web'
  scope: rg
  params: {
    principalId: web.outputs.principalId
    roleDefinitionId: '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull
    principalType: 'ServicePrincipal'
  }
}

module acrPullJob './modules/role-assignment.bicep' = {
  name: 'acr-pull-job'
  scope: rg
  params: {
    principalId: job.outputs.principalId
    roleDefinitionId: '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull
    principalType: 'ServicePrincipal'
  }
}

module acrPullCollector './modules/role-assignment.bicep' = {
  name: 'acr-pull-collector'
  scope: rg
  params: {
    principalId: ondemand.outputs.principalId
    roleDefinitionId: '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull
    principalType: 'ServicePrincipal'
  }
}

// Role assignments - Key Vault Secrets User for container apps
module kvSecretsWeb './modules/role-assignment.bicep' = {
  name: 'kv-secrets-web'
  scope: rg
  params: {
    principalId: web.outputs.principalId
    roleDefinitionId: '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
    principalType: 'ServicePrincipal'
  }
}

module kvSecretsJob './modules/role-assignment.bicep' = {
  name: 'kv-secrets-job'
  scope: rg
  params: {
    principalId: job.outputs.principalId
    roleDefinitionId: '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
    principalType: 'ServicePrincipal'
  }
}

module kvSecretsCollector './modules/role-assignment.bicep' = {
  name: 'kv-secrets-collector'
  scope: rg
  params: {
    principalId: ondemand.outputs.principalId
    roleDefinitionId: '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
    principalType: 'ServicePrincipal'
  }
}

output AZURE_CONTAINER_REGISTRY_ENDPOINT string = acr.outputs.loginServer
output AZURE_CONTAINER_REGISTRY_NAME string = acr.outputs.name
output COLLECTOR_FQDN string = ondemand.outputs.fqdn
output WEB_URI string = web.outputs.uri
