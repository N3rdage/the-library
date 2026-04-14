// Re-PUTs the hostname binding with SslState=SniEnabled and the issued
// certificate thumbprint. Must be a separate module so Bicep lets us declare
// the same ARM resource twice (once empty, once with SSL attached).
param appServiceName string
param customDomain string
param thumbprint string

resource app 'Microsoft.Web/sites@2023-12-01' existing = {
  name: appServiceName
}

resource binding 'Microsoft.Web/sites/hostNameBindings@2023-12-01' = {
  parent: app
  name: customDomain
  properties: {
    siteName: appServiceName
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: thumbprint
  }
}
