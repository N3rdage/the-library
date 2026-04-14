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

// Short suffix to keep globally-unique names (App Service hostname, SQL server
// name) stable across re-deploys while still being unique per-subscription.
var uniqueSuffix = substring(uniqueString(resourceGroup().id), 0, 6)
var appServiceName = '${appName}-${uniqueSuffix}'
var appServicePlanName = '${appName}-plan'
var sqlServerName = '${appName}-sql-${uniqueSuffix}'
var sqlDatabaseName = appName
var logAnalyticsName = '${appName}-logs'
var appInsightsName = '${appName}-ai'

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
    aadAdminObjectId: sqlAadAdminObjectId
    aadAdminLogin: sqlAadAdminLogin
    tenantId: tenantId
  }
}

module app './appservice.bicep' = {
  name: 'app'
  params: {
    location: location
    tags: tags
    appServiceName: appServiceName
    appServicePlanName: appServicePlanName
    tenantId: tenantId
    authClientId: authClientId
    authClientSecret: authClientSecret
    sqlServerFqdn: sql.outputs.sqlServerFqdn
    sqlDatabaseName: sqlDatabaseName
    appInsightsConnectionString: obs.outputs.appInsightsConnectionString
  }
}

output appServiceUrl string = app.outputs.appServiceUrl
output appServiceName string = app.outputs.appServiceName
output appServicePrincipalId string = app.outputs.principalId
output stagingHostName string = app.outputs.stagingHostName
output stagingPrincipalId string = app.outputs.stagingPrincipalId
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output sqlDatabaseName string = sqlDatabaseName
