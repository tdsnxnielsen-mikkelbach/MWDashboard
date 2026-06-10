@description('Name of the Container App (Consent Callback)')
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

@description('Allowed CORS origin (Static Web App URL)')
param corsOrigin string

var fullImageName = imageName != '' ? '${containerRegistryLoginServer}/${imageName}' : 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

resource containerAppConsent 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: union(tags, { 'azd-service-name': 'consent' })
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
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        corsPolicy: {
          allowedOrigins: [corsOrigin]
          allowedMethods: ['POST']
          allowedHeaders: ['*']
        }
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
        {
          name: 'consent-shared-secret'
          keyVaultUrl: '${keyVaultUri}secrets/ConsentSharedSecret'
          identity: managedIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'consent'
          image: fullImageName
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
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
              name: 'ConsentCallback__SharedSecret'
              secretRef: 'consent-shared-secret'
            }
            {
              name: 'Cors__AllowedOrigins__0'
              value: corsOrigin
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '10'
              }
            }
          }
        ]
      }
    }
  }
}

output id string = containerAppConsent.id
output name string = containerAppConsent.name
output fqdn string = containerAppConsent.properties.configuration.ingress.fqdn
output principalId string = containerAppConsent.identity.principalId
