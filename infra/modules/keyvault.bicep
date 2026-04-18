param location string
param tags object
param keyVaultName string
param tenantId string

// Principal IDs that need Secrets User access (App Service + staging slot)
param appServicePrincipalId string
param stagingSlotPrincipalId string

// Secrets to store
@secure()
param authClientSecret string
@secure()
param aiAnthropicApiKey string = ''
@secure()
param aiMicrosoftFoundryApiKey string = ''
@secure()
param aiAzureOpenAIApiKey string = ''

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
    networkAcls: {
      defaultAction: 'Allow'  // Will be restricted to VNet once Private Endpoint is added
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

// Secrets
resource secretAuthClient 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: 'AuthClientSecret'
  properties: {
    value: authClientSecret
  }
}

resource secretAnthropicApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(aiAnthropicApiKey)) {
  parent: kv
  name: 'AIAnthropicApiKey'
  properties: {
    value: aiAnthropicApiKey
  }
}

resource secretMicrosoftFoundryApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(aiMicrosoftFoundryApiKey)) {
  parent: kv
  name: 'AIMicrosoftFoundryApiKey'
  properties: {
    value: aiMicrosoftFoundryApiKey
  }
}

resource secretAzureOpenAIApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(aiAzureOpenAIApiKey)) {
  parent: kv
  name: 'AIAzureOpenAIApiKey'
  properties: {
    value: aiAzureOpenAIApiKey
  }
}

output keyVaultName string = kv.name
output keyVaultUri string = kv.properties.vaultUri

// Key Vault references for App Settings (use these instead of raw secrets)
output authClientSecretRef string = '@Microsoft.KeyVault(SecretUri=${secretAuthClient.properties.secretUri})'
output aiAnthropicApiKeyRef string = !empty(aiAnthropicApiKey) ? '@Microsoft.KeyVault(SecretUri=${secretAnthropicApiKey.properties.secretUri})' : ''
output aiMicrosoftFoundryApiKeyRef string = !empty(aiMicrosoftFoundryApiKey) ? '@Microsoft.KeyVault(SecretUri=${secretMicrosoftFoundryApiKey.properties.secretUri})' : ''
output aiAzureOpenAIApiKeyRef string = !empty(aiAzureOpenAIApiKey) ? '@Microsoft.KeyVault(SecretUri=${secretAzureOpenAIApiKey.properties.secretUri})' : ''
