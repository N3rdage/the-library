param location string
param tags object
param keyVaultName string
param tenantId string

// Principal IDs that need Secrets User access (App Service + staging slot)
param appServicePrincipalId string
param stagingSlotPrincipalId string

// Externally-supplied secrets. AI provider keys for Foundry/OpenAI are NOT
// here — those are written by ai-services.bicep using listKeys() against the
// resources it provisions, which keeps them out of deployment params/logs.
@secure()
param authClientSecret string
@secure()
param anthropicApiKey string = ''
@secure()
param troveApiKey string = ''

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    // Public access is denied — App Service reaches KV via Private Endpoint
    // through VNet integration. AzureServices bypass is left on so platform
    // services (e.g. ARM during the deployment itself) can still write
    // secrets while resources are being provisioned.
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

// Key Vault Secrets User role for App Service managed identity
resource appSecretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, appServicePrincipalId, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Key Vault Secrets User role for staging slot managed identity
resource stagingSecretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, stagingSlotPrincipalId, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId: stagingSlotPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource secretAuthClient 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: 'AuthClientSecret'
  properties: {
    value: authClientSecret
  }
}

resource secretAnthropicApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(anthropicApiKey)) {
  parent: kv
  name: 'AIAnthropicApiKey'
  properties: {
    value: anthropicApiKey
  }
}

resource secretTroveApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(troveApiKey)) {
  parent: kv
  name: 'TroveApiKey'
  properties: {
    value: troveApiKey
  }
}

output keyVaultId string = kv.id
output keyVaultName string = kv.name
output keyVaultUri string = kv.properties.vaultUri
