// Creates the two halves of a VNet peering. Both VNets must already exist.
// Each peering resource lives under its own VNet, so we declare them as nested
// resources of an `existing` parent reference.
param primaryVnetName string
param secondaryVnetName string
param secondaryVnetId string
param primaryVnetId string

resource primaryVnet 'Microsoft.Network/virtualNetworks@2024-01-01' existing = {
  name: primaryVnetName
}

resource secondaryVnet 'Microsoft.Network/virtualNetworks@2024-01-01' existing = {
  name: secondaryVnetName
}

resource primaryToSecondary 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings@2024-01-01' = {
  parent: primaryVnet
  name: 'to-${secondaryVnetName}'
  properties: {
    allowVirtualNetworkAccess: true
    allowForwardedTraffic: false
    allowGatewayTransit: false
    useRemoteGateways: false
    remoteVirtualNetwork: {
      id: secondaryVnetId
    }
  }
}

resource secondaryToPrimary 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings@2024-01-01' = {
  parent: secondaryVnet
  name: 'to-${primaryVnetName}'
  properties: {
    allowVirtualNetworkAccess: true
    allowForwardedTraffic: false
    allowGatewayTransit: false
    useRemoteGateways: false
    remoteVirtualNetwork: {
      id: primaryVnetId
    }
  }
}
