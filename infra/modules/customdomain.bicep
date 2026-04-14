// Adds a custom hostname + App Service Managed Certificate to the production
// slot. Pattern:
//   1. Create the hostname binding with SSL disabled (App Service validates
//      DNS against the site's customDomainVerificationId + CNAME target).
//   2. Create the Microsoft.Web/certificates resource (managed cert, auto-
//      renewing). Azure issues it by re-validating DNS from the binding.
//   3. Re-PUT the binding via a nested module, this time with
//      sslState=SniEnabled + the freshly-issued cert thumbprint.
// Step 3 lives in bindssl.bicep so the binding can be "re-declared" (ARM
// treats it as an update; Bicep can't have two declarations of the same
// resource in a single module).
param appServiceName string
param customDomain string
param location string
param tags object

resource app 'Microsoft.Web/sites@2023-12-01' existing = {
  name: appServiceName
}

resource binding 'Microsoft.Web/sites/hostNameBindings@2023-12-01' = {
  parent: app
  name: customDomain
  properties: {
    siteName: appServiceName
    hostNameType: 'Verified'
    sslState: 'Disabled'
    customHostNameDnsRecordType: 'CName'
  }
}

// App Service Managed Certificate — free, auto-renewing. CNAME-pointed
// subdomains only (books.silly.ninja qualifies; apex domains do not).
resource cert 'Microsoft.Web/certificates@2023-12-01' = {
  name: replace(customDomain, '.', '-')
  location: location
  tags: tags
  properties: {
    canonicalName: customDomain
    serverFarmId: app.properties.serverFarmId
  }
  dependsOn: [
    binding
  ]
}

module bindSsl './bindssl.bicep' = {
  name: 'bindSsl-${uniqueString(customDomain)}'
  params: {
    appServiceName: appServiceName
    customDomain: customDomain
    thumbprint: cert.properties.thumbprint
  }
}

output certThumbprint string = cert.properties.thumbprint
