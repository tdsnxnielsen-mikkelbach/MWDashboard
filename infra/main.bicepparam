using './main.bicep'

param environmentName = readEnvironmentVariable('AZURE_ENV_NAME', 'dev')
param location = readEnvironmentVariable('AZURE_LOCATION', 'swedencentral')
param azureAdClientId = readEnvironmentVariable('AZURE_AD_CLIENT_ID', '')
param azureAdClientSecret = readEnvironmentVariable('AZURE_AD_CLIENT_SECRET', '')
param azureAdTenantId = readEnvironmentVariable('AZURE_AD_TENANT_ID', '')
param consentSharedSecret = readEnvironmentVariable('CONSENT_SHARED_SECRET', '')
