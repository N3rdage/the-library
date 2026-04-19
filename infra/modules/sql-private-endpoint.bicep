param location string
param tags object

@description('Resource ID of the VNet hosting the private endpoint subnet. Used to link the Private DNS Zone.')
param vnetId string

@description('Resource ID of the subnet in which the Private Endpoint NIC will be created.')
param privateEndpointSubnetId string

@description('Resource ID of the SQL server to expose privately.')
param sqlServerId string

@description('Name to give the Private Endpoint resource.')
param privateEndpointName string

// Private Link DNS zone name is fixed per-cloud; this project targets Azure
// Public, so the literal is correct.
#disable-next-line no-hardcoded-env-urls
var privateDnsZoneName = 'privatelink.database.windows.net'

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: privateDnsZoneName
  location: 'global'
  tags: tags
}

resource dnsVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: privateDnsZone
  name: 'vnet-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnetId
    }
  }
}

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2024-01-01' = {
  name: privateEndpointName
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'sql'
        properties: {
          privateLinkServiceId: sqlServerId
          groupIds: [
            'sqlServer'
          ]
        }
      }
    ]
  }
}

// Auto-register the PE's IP into the Private DNS Zone so the SQL FQDN resolves
// privately from the VNet without manual A-record management.
resource peDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-01-01' = {
  parent: privateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'database-windows-net'
        properties: {
          privateDnsZoneId: privateDnsZone.id
        }
      }
    ]
  }
}

output privateEndpointId string = privateEndpoint.id
output privateDnsZoneId string = privateDnsZone.id
