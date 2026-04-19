// App Service plan, site, and staging slot only. Configuration (app settings,
// connection string, Easy Auth) lives in app-config.bicep so it can run after
// dependencies (Key Vault, AI services) are in place — otherwise we'd have a
// reference cycle (AI services need the app's principal IDs; app settings
// need the AI endpoints).
param location string
param tags object
param appServiceName string
param appServicePlanName string
param appIntegrationSubnetId string

// Linux S1: AlwaysOn + slots available, suits Blazor Server.
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'S1'
    tier: 'Standard'
    capacity: 1
  }
  properties: {
    reserved: true
  }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appServiceName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: true
    virtualNetworkSubnetId: appIntegrationSubnetId
    vnetRouteAllEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      webSocketsEnabled: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }
}

// Staging deployment slot — GitHub Actions deploys here; swap.yml promotes to prod.
// Each slot gets its own system-assigned managed identity, so both need to be
// granted on the SQL DB (deploy.ps1 handles both).
resource stagingSlot 'Microsoft.Web/sites/slots@2023-12-01' = {
  parent: app
  name: 'staging'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: true
    virtualNetworkSubnetId: appIntegrationSubnetId
    vnetRouteAllEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      webSocketsEnabled: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }
}

output appServiceUrl string = 'https://${app.properties.defaultHostName}'
output appServiceName string = app.name
output defaultHostName string = app.properties.defaultHostName
output customDomainVerificationId string = app.properties.customDomainVerificationId
output principalId string = app.identity.principalId
output stagingHostName string = stagingSlot.properties.defaultHostName
output stagingPrincipalId string = stagingSlot.identity.principalId
output stagingSlotName string = stagingSlot.name
