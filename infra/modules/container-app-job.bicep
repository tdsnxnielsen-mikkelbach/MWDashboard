@description('Name of the Container App Job')
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

var fullImageName = imageName != '' ? '${containerRegistryLoginServer}/${imageName}' : 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

resource containerAppJob 'Microsoft.App/jobs@2024-03-01' = {
  name: name
  location: location
  tags: union(tags, { 'azd-service-name': 'collector' })
  identity: {
    type: 'SystemAssigned,UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironmentId
    configuration: {
      triggerType: 'Schedule'
      scheduleTriggerConfig: {
        cronExpression: '0 2 * * *' // Run daily at 2:00 AM UTC
        parallelism: 1
        replicaCompletionCount: 1
      }
      replicaTimeout: 3600 // 1 hour max
      replicaRetryLimit: 1
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
    }
  }
}

output id string = containerAppJob.id
output name string = containerAppJob.name
output principalId string = containerAppJob.identity.principalId
