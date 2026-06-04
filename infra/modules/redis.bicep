@description('Name of the Redis Enterprise cluster')
param name string

@description('Location for the resource')
param location string

@description('Tags for the resource')
param tags object = {}

resource redisEnterprise 'Microsoft.Cache/redisEnterprise@2024-09-01-preview' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Balanced_B0'
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource redisDatabase 'Microsoft.Cache/redisEnterprise/databases@2024-09-01-preview' = {
  parent: redisEnterprise
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    evictionPolicy: 'VolatileLRU'
    clusteringPolicy: 'OSSCluster'
    port: 10000
  }
}

output id string = redisEnterprise.id
output name string = redisEnterprise.name

#disable-next-line outputs-should-not-contain-secrets // Connection string is stored in Key Vault, not exposed
output connectionString string = '${redisEnterprise.properties.hostName}:${redisDatabase.properties.port},password=${redisDatabase.listKeys().primaryKey},ssl=True,abortConnect=False'
