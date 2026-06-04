@description('Name of the SQL Server')
param name string

@description('Location for the resource')
param location string

@description('Tags for the resource')
param tags object = {}

@description('Name of the database')
param databaseName string

@description('Object ID (principal ID) of the User-Assigned Managed Identity for AD admin')
param adminObjectId string

@description('Name (login) for the AD admin identity')
param adminLoginName string

@description('Client ID of the User-Assigned Managed Identity for connection string')
param managedIdentityClientId string

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: name
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    administrators: {
      azureADOnlyAuthentication: true
      administratorType: 'ActiveDirectory'
      login: adminLoginName
      principalType: 'Application'
      sid: adminObjectId
      tenantId: tenant().tenantId
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5_1'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 34359738368
    autoPauseDelay: 60
    minCapacity: json('0.5')
  }
}

// Allow Azure services to access the SQL Server
resource firewallRule 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output id string = sqlServer.id
output name string = sqlServer.name
output connectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Default;User Id=${managedIdentityClientId};Encrypt=True;TrustServerCertificate=False;'
