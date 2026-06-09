@description('Name of the Container App (Collector)')
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

var fullImageName = imageName != '' ? '${containerRegistryLoginServer}/${imageName}' : 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
var ingressPort = imageName != '' ? 8080 : 80

resource containerAppCollector 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: union(tags, { 'azd-service-name': 'ondemand' })
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
        targetPort: ingressPort
        transport: 'auto'
      }
      registries: [
        {
          server: containerRegistryLoginServer
          identity: 'system'
        }
      ]
      secrets: [
        {
          name: 'azuread-client-id'
          keyVaultUrl: '${keyVaultUri}secrets/AzureAdClientId'
          identity: 'system'
        }
        {
          name: 'azuread-client-secret'
          keyVaultUrl: '${keyVaultUri}secrets/AzureAdClientSecret'
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'collector'
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
          ]
        }
      ]
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

output id string = containerAppCollector.id
output name string = containerAppCollector.name
output fqdn string = containerAppCollector.properties.configuration.ingress.fqdn
output principalId string = containerAppCollector.identity.principalId
