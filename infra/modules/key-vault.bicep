@description('Name of the Key Vault')
param name string

@description('Location for the resource')
param location string

@description('Tags for the resource')
param tags object = {}

@description('Azure AD Tenant ID')
param tenantId string = tenant().tenantId

@description('Azure AD Client Secret to store')
@secure()
param azureAdClientSecret string

@description('Azure AD Client ID to store')
@secure()
param azureAdClientId string

@description('Redis connection string to store')
@secure()
param redisConnectionString string

@description('Consent callback shared secret to store')
@secure()
param consentSharedSecret string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

resource secretAdClientId 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'AzureAdClientId'
  properties: {
    value: azureAdClientId
  }
}

resource secretAdClientSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'AzureAdClientSecret'
  properties: {
    value: azureAdClientSecret
  }
}

resource secretRedisConnection 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'RedisConnectionString'
  properties: {
    value: redisConnectionString
  }
}

resource secretConsentShared 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ConsentSharedSecret'
  properties: {
    value: consentSharedSecret
  }
}

output id string = keyVault.id
output name string = keyVault.name
output uri string = keyVault.properties.vaultUri
