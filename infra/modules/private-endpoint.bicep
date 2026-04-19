// Generic Private Endpoint module: creates the Private DNS Zone (if it
// doesn't exist), links it to one or more VNets, creates the PE in the
// supplied subnet, and registers the PE's IP into the zone via a DNS zone
// group. Reused for Key Vault, Cognitive Services, Azure OpenAI, etc.

param location string
param tags object

@description('Private DNS zone name, e.g. privatelink.vaultcore.azure.net')
param privateDnsZoneName string

@description('Resource IDs of the VNets that should be able to resolve names in this zone (typically the App Service\'s VNet).')
param linkedVnetIds array

@description('Subnet ID where the Private Endpoint NIC will be created.')
param privateEndpointSubnetId string

@description('Resource ID of the target service to expose privately.')
param targetResourceId string

@description('Private Link group ID for the target (e.g. sqlServer, vault, account).')
param groupId string

@description('Name to give the Private Endpoint resource.')
param privateEndpointName string

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: privateDnsZoneName
  location: 'global'
  tags: tags
}

resource dnsVnetLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (vnetId, i) in linkedVnetIds: {
  parent: privateDnsZone
  name: 'vnet-link-${i}'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnetId
    }
  }
}]

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
        name: 'connection'
        properties: {
          privateLinkServiceId: targetResourceId
          groupIds: [
            groupId
          ]
        }
      }
    ]
  }
}

resource peDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-01-01' = {
  parent: privateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: replace(privateDnsZoneName, '.', '-')
        properties: {
          privateDnsZoneId: privateDnsZone.id
        }
      }
    ]
  }
}

output privateEndpointId string = privateEndpoint.id
output privateDnsZoneId string = privateDnsZone.id
