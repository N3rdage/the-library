param location string
param tags object
param sqlServerName string
param sqlDatabaseName string
param tenantId string

@description('Object ID of the user/group set as the SQL AAD admin. This principal must run deploy.ps1 to grant the App Service managed identity access to the DB.')
param aadAdminObjectId string

@description('Display name shown in the portal for the AAD admin. Cosmetic only.')
param aadAdminLogin string

// AAD-only auth: no SQL logins exist. The admin above is the only way in until
// deploy.ps1 provisions the App Service managed identity as a SQL user.
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: aadAdminLogin
      sid: aadAdminObjectId
      tenantId: tenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648
  }
}

// Allow other Azure services (incl. the App Service) to reach the SQL server.
// Suitable for this small app; swap to Private Endpoint / VNet integration for stricter isolation.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlServerName string = sqlServer.name
