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

@description('API docs (Scalar/OpenAPI) access key to store')
@secure()
param apiDocsKey string

@description('Read API (programmatic JSON data API) admin key to store')
@secure()
param readApiKey string

@description('TD SYNNEX Ion Gateway API key (pricing/margin enrichment) to store')
@secure()
param ionGatewayApiKey string = ''

@description('Partner Center Transfer API key (reseller/customer/subscription enrichment) to store')
@secure()
param partnerCenterApiKey string = ''

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

resource secretApiDocsKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ApiDocsKey'
  properties: {
    value: apiDocsKey
  }
}

resource secretReadApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ReadApiKey'
  properties: {
    value: readApiKey
  }
}

resource secretIonGatewayApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'IonGatewayApiKey'
  properties: {
    value: ionGatewayApiKey
  }
}

resource secretPartnerCenterApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'PartnerCenterApiKey'
  properties: {
    value: partnerCenterApiKey
  }
}

output id string = keyVault.id
output name string = keyVault.name
output uri string = keyVault.properties.vaultUri
