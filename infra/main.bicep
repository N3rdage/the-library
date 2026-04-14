// Subscription-scope entry point. Creates the resource group and delegates
// everything inside it to the `resources` module.
targetScope = 'subscription'

@description('Azure region for the resource group and all resources.')
param location string = 'australiaeast'

@description('Base name used for resource naming and tags.')
param appName string = 'booktracker'

@description('Entra tenant ID (GUID).')
param tenantId string

@description('App (client) ID of the Entra app registration used by Easy Auth.')
param authClientId string

@description('Client secret for the Easy Auth app registration. Created/rotated by deploy.ps1.')
@secure()
param authClientSecret string

@description('Object ID of the Entra user/group to set as the SQL Server AAD admin.')
param sqlAadAdminObjectId string

@description('Display name / UPN of the SQL Server AAD admin. Shown in the portal only.')
param sqlAadAdminLogin string

var tags = {
  Client: 'Drew'
  Environment: 'Production'
}

var rgName = 'rg-${appName}-prod'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: rgName
  location: location
  tags: tags
}

module resources './modules/resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    location: location
    appName: appName
    tags: tags
    tenantId: tenantId
    authClientId: authClientId
    authClientSecret: authClientSecret
    sqlAadAdminObjectId: sqlAadAdminObjectId
    sqlAadAdminLogin: sqlAadAdminLogin
  }
}

output resourceGroupName string = rg.name
output appServiceUrl string = resources.outputs.appServiceUrl
output appServiceName string = resources.outputs.appServiceName
output appServicePrincipalId string = resources.outputs.appServicePrincipalId
output stagingHostName string = resources.outputs.stagingHostName
output stagingPrincipalId string = resources.outputs.stagingPrincipalId
output sqlServerFqdn string = resources.outputs.sqlServerFqdn
output sqlDatabaseName string = resources.outputs.sqlDatabaseName
