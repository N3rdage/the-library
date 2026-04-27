param location string
param tags object
param sqlServerName string
param sqlDatabaseName string
param stagingSqlDatabaseName string
param tenantId string

@description('Object ID of the user/group set as the SQL AAD admin. This principal must run deploy.ps1 to grant the App Service managed identity access to the DB.')
param aadAdminObjectId string

@description('Display name shown in the portal for the AAD admin. Cosmetic only.')
param aadAdminLogin string

@description('Optional public IPv4 address allowed through the SQL firewall for ad-hoc access (e.g. local EF migrations). Leave blank to keep SQL fully private.')
param devClientIp string = ''

// AAD-only auth: no SQL logins exist. The admin above is the only way in until
// deploy.ps1 provisions the App Service managed identity as a SQL user.
// Public network access is disabled — App Service reaches SQL via VNet
// integration + Private Endpoint. An optional firewall rule still applies to
// the public endpoint when re-enabled, which is why we expose devClientIp.
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: empty(devClientIp) ? 'Disabled' : 'Enabled'
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

// Separate database for the staging slot so deploys against staging can't
// touch prod data (a destructive migration would otherwise take prod down
// before any swap, and slot swap-back would give old binaries against the
// new schema). Same Basic SKU as prod for symmetric provisioning shape;
// AAD-only auth and Private Endpoint are inherited from the parent server.
// First deploy lands an empty DB — migrate-on-startup creates the schema
// against it on the next app start. See infra/README.md for ordering.
resource sqlStagingDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: stagingSqlDatabaseName
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

// Optional pinhole for local development (EF migrations from a laptop).
// Only created when devClientIp is supplied; deploys without it leave SQL
// reachable solely through the Private Endpoint.
resource devClientFirewallRule 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (!empty(devClientIp)) {
  parent: sqlServer
  name: 'DevClient'
  properties: {
    startIpAddress: devClientIp
    endIpAddress: devClientIp
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlServerName string = sqlServer.name
output sqlServerId string = sqlServer.id
output sqlDatabaseName string = sqlDb.name
output stagingSqlDatabaseName string = sqlStagingDb.name
