@description('Name of the Container App (Copilot Audit collector)')
param name string

@description('Location for the resource')
param location string

@description('Tags for the resource')
param tags object = {}

@description('Container Apps Environment ID')
param containerAppsEnvironmentId string

@description('Container Registry login server')
param containerRegistryLoginServer string

@description('Container image name (empty string uses placeholder for initial deploy)')
param imageName string

@description('SQL Server connection string')
param sqlConnectionString string

@description('Key Vault URI')
param keyVaultUri string

@description('Azure AD Tenant ID')
param azureAdTenantId string

@description('User-Assigned Managed Identity resource ID')
param managedIdentityId string

@description('Application Insights connection string')
param appInsightsConnectionString string = ''

// On first provision the image does not exist in ACR yet, so fall back to a public placeholder
// image. azd then pushes the real image and re-deploys. This prevents the initial deploy hanging
// on an image-pull failure (same pattern as the on-demand collector container app).
var fullImageName = imageName != '' ? '${containerRegistryLoginServer}/${imageName}' : 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

resource containerAppCopilotAudit 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: union(tags, { 'azd-service-name': 'copilotaudit' })
  identity: {
    type: 'SystemAssigned,UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: containerRegistryLoginServer
          identity: managedIdentityId
        }
      ]
      secrets: [
        {
          name: 'azuread-client-id'
          keyVaultUrl: '${keyVaultUri}secrets/AzureAdClientId'
          identity: managedIdentityId
        }
        {
          name: 'azuread-client-secret'
          keyVaultUrl: '${keyVaultUri}secrets/AzureAdClientSecret'
          identity: managedIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'copilotaudit'
          image: fullImageName
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__DefaultConnection'
              value: sqlConnectionString
            }
            {
              name: 'AzureAd__ClientId'
              secretRef: 'azuread-client-id'
            }
            {
              name: 'AzureAd__ClientSecret'
              secretRef: 'azuread-client-secret'
            }
            {
              name: 'AzureAd__TenantId'
              value: azureAdTenantId
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
          ]
        }
      ]
      // minReplicas must stay at 1: the internal cron scheduler that advances each tenant's
      // 7-day audit cursor only runs while a replica is alive.
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '5'
              }
            }
          }
        ]
      }
    }
  }
}

output id string = containerAppCopilotAudit.id
output name string = containerAppCopilotAudit.name
output fqdn string = containerAppCopilotAudit.properties.configuration.ingress.fqdn
output principalId string = containerAppCopilotAudit.identity.principalId
