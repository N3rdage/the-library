// Orchestrates the resource-group-scoped stack.
param location string
param appName string
param tags object
param tenantId string
param authClientId string
@secure()
param authClientSecret string
param sqlAadAdminObjectId string
param sqlAadAdminLogin string

@description('Optional custom hostname to bind to the production slot (e.g. books.silly.ninja). Leave blank to skip.')
param customDomain string = ''

@description('Optional public IPv4 address allowed through the SQL firewall for ad-hoc access. Leave blank to keep SQL fully private.')
param devClientIp string = ''

@description('Region for Azure-hosted AI services (Azure OpenAI). Defaults to eastus2 because the gpt-4o deployment in australiaeast is being retired in June 2026.')
param secondaryLocation string = 'eastus2'

@description('Optional Anthropic public-API key. When supplied it is stored in Key Vault and exposed as the AI__Anthropic__ApiKey app setting via a KV reference.')
@secure()
param anthropicApiKey string = ''

@description('Optional Trove (NLA) API key. When supplied it is stored in Key Vault and exposed as the Trove__ApiKey app setting via a KV reference.')
@secure()
param troveApiKey string = ''

// Short suffix to keep globally-unique names (App Service hostname, SQL server
// name) stable across re-deploys while still being unique per-subscription.
var uniqueSuffix = substring(uniqueString(resourceGroup().id), 0, 6)
var appServiceName = '${appName}-${uniqueSuffix}'
var appServicePlanName = '${appName}-plan'
var sqlServerName = '${appName}-sql-${uniqueSuffix}'
var sqlDatabaseName = appName
var stagingSqlDatabaseName = '${appName}-staging'
var vnetName = '${appName}-vnet'
var vnetSecondaryName = '${appName}-vnet-${secondaryLocation}'
var keyVaultName = '${appName}-kv-${uniqueSuffix}'
var logAnalyticsName = '${appName}-logs'
var appInsightsName = '${appName}-ai'
var openAIAccountName = '${appName}-openai-${uniqueSuffix}'

module network './network.bicep' = {
  name: 'network'
  params: {
    location: location
    tags: tags
    vnetName: vnetName
  }
}

module networkSecondary './network-secondary.bicep' = {
  name: 'networkSecondary'
  params: {
    location: secondaryLocation
    tags: tags
    vnetName: vnetSecondaryName
  }
}

module peering './vnet-peering.bicep' = {
  name: 'peering'
  params: {
    primaryVnetName: network.outputs.vnetName
    secondaryVnetName: networkSecondary.outputs.vnetName
    primaryVnetId: network.outputs.vnetId
    secondaryVnetId: networkSecondary.outputs.vnetId
  }
}

module obs './observability.bicep' = {
  name: 'obs'
  params: {
    location: location
    tags: tags
    logAnalyticsName: logAnalyticsName
    appInsightsName: appInsightsName
  }
}

module sql './sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    tags: tags
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    stagingSqlDatabaseName: stagingSqlDatabaseName
    aadAdminObjectId: sqlAadAdminObjectId
    aadAdminLogin: sqlAadAdminLogin
    tenantId: tenantId
    devClientIp: devClientIp
  }
}

module sqlPe './sql-private-endpoint.bicep' = {
  name: 'sqlPe'
  params: {
    location: location
    tags: tags
    vnetId: network.outputs.vnetId
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    sqlServerId: sql.outputs.sqlServerId
    privateEndpointName: '${sqlServerName}-pe'
  }
}

// App Service is just plan + site + slot here so its identity exists for
// downstream RBAC (KV, AI services). All actual configuration (app settings,
// connection strings, Easy Auth) is applied later by app-config.bicep, after
// KV and AI services are in place. This avoids a reference cycle where AI
// services need the app's principal IDs while app settings need the AI
// endpoints.
module app './appservice.bicep' = {
  name: 'app'
  params: {
    location: location
    tags: tags
    appServiceName: appServiceName
    appServicePlanName: appServicePlanName
    appIntegrationSubnetId: network.outputs.appIntegrationSubnetId
  }
}

module kv './keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    tags: tags
    keyVaultName: keyVaultName
    tenantId: tenantId
    appServicePrincipalId: app.outputs.principalId
    stagingSlotPrincipalId: app.outputs.stagingPrincipalId
    authClientSecret: authClientSecret
    anthropicApiKey: anthropicApiKey
    troveApiKey: troveApiKey
  }
}

module kvPe './private-endpoint.bicep' = {
  name: 'kvPe'
  params: {
    location: location
    tags: tags
    privateDnsZoneName: 'privatelink.vaultcore.azure.net'
    linkedVnetIds: [
      network.outputs.vnetId
      networkSecondary.outputs.vnetId
    ]
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    targetResourceId: kv.outputs.keyVaultId
    groupId: 'vault'
    privateEndpointName: '${keyVaultName}-pe'
  }
}

// Azure OpenAI lives in the secondary region. The KV that holds its API key
// is in the primary region — that's fine, KV cross-region writes work as long
// as the deploying principal has RBAC.
module ai './ai-services.bicep' = {
  name: 'ai'
  params: {
    location: secondaryLocation
    tags: tags
    openAIAccountName: openAIAccountName
    keyVaultName: keyVaultName
    openAICustomSubDomain: openAIAccountName
    appPrincipalIds: [
      app.outputs.principalId
      app.outputs.stagingPrincipalId
    ]
  }
  // KV must exist before ai-services tries to write secrets into it.
  dependsOn: [
    kv
  ]
}

module openAIPe './private-endpoint.bicep' = {
  name: 'openAIPe'
  params: {
    location: secondaryLocation
    tags: tags
    privateDnsZoneName: 'privatelink.openai.azure.com'
    linkedVnetIds: [
      network.outputs.vnetId
      networkSecondary.outputs.vnetId
    ]
    privateEndpointSubnetId: networkSecondary.outputs.privateEndpointSubnetId
    targetResourceId: ai.outputs.openAIId
    groupId: 'account'
    privateEndpointName: '${openAIAccountName}-pe'
  }
}

module appConfig './app-config.bicep' = {
  name: 'appConfig'
  params: {
    appServiceName: app.outputs.appServiceName
    stagingSlotName: app.outputs.stagingSlotName
    tenantId: tenantId
    authClientId: authClientId
    sqlServerFqdn: sql.outputs.sqlServerFqdn
    sqlDatabaseName: sqlDatabaseName
    stagingSqlDatabaseName: stagingSqlDatabaseName
    appInsightsConnectionString: obs.outputs.appInsightsConnectionString
    keyVaultName: keyVaultName
    aiAzureOpenAIEndpoint: ai.outputs.openAIEndpoint
    aiAzureOpenAIDeployment: ai.outputs.openAIDeploymentName
  }
  // Wait for KV (so KV refs resolve) and AI deployments (so endpoints exist).
  dependsOn: [
    kv
  ]
}

module customdomain './customdomain.bicep' = if (!empty(customDomain)) {
  name: 'customdomain'
  params: {
    appServiceName: app.outputs.appServiceName
    customDomain: customDomain
    location: location
    tags: tags
  }
}

output appServiceUrl string = app.outputs.appServiceUrl
output appServiceName string = app.outputs.appServiceName
output defaultHostName string = app.outputs.defaultHostName
output customDomainVerificationId string = app.outputs.customDomainVerificationId
output appServicePrincipalId string = app.outputs.principalId
output stagingHostName string = app.outputs.stagingHostName
output stagingPrincipalId string = app.outputs.stagingPrincipalId
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output sqlDatabaseName string = sqlDatabaseName
output stagingSqlDatabaseName string = stagingSqlDatabaseName
output keyVaultName string = kv.outputs.keyVaultName
output keyVaultUri string = kv.outputs.keyVaultUri
output vnetName string = network.outputs.vnetName
output privateEndpointSubnetId string = network.outputs.privateEndpointSubnetId
output secondaryVnetName string = networkSecondary.outputs.vnetName
output openAIEndpoint string = ai.outputs.openAIEndpoint
