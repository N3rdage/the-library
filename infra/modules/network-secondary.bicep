param location string
param tags object
param vnetName string

// Secondary VNet that hosts Private Endpoints for resources only available in
// regions other than the primary (e.g. Microsoft Foundry / Azure OpenAI in
// eastus2). Address space must not overlap with the primary VNet.
resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.1.0.0/16'
      ]
    }
    subnets: [
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: '10.1.2.0/24'
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output privateEndpointSubnetId string = vnet.properties.subnets[0].id
