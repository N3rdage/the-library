// Azure OpenAI account (kind=OpenAI). Deployed in a region that hosts the
// desired models — for this project that's eastus2 because the gpt-4o
// deployment in australiaeast is being retired in June 2026.
//
// Microsoft Foundry / Claude is intentionally NOT deployed here: this
// project's Azure subscription is Sponsored, which Microsoft excludes from
// Claude eligibility on Foundry. Direct Anthropic API (public, non-Azure)
// remains the AI provider of choice for Claude. See TODO.md for the
// follow-up to add Foundry once on an EA / MCA-E subscription.
//
// Public network access is disabled — the App Service reaches OpenAI
// through VNet integration + a Private Endpoint in a peered eastus2 VNet.
//
// API keys are written into Key Vault by this module (using listKeys + an
// existing reference to KV) so the App Settings can use Key Vault references
// instead of raw secrets.

param location string
param tags object
param openAIAccountName string
param keyVaultName string

@description('Custom subdomain for the Azure OpenAI account. Required for AAD auth and used in the resource\'s endpoint URL.')
param openAICustomSubDomain string

@description('Principal IDs of the App Service slot identities to grant Cognitive Services User on the OpenAI account.')
param appPrincipalIds array

resource openAI 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAIAccountName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: openAICustomSubDomain
    publicNetworkAccess: 'Disabled'
    disableLocalAuth: false
    networkAcls: {
      defaultAction: 'Deny'
      virtualNetworkRules: []
      ipRules: []
    }
  }
}

resource openAIDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAI
  name: 'gpt-4o'
  sku: {
    name: 'Standard'
    capacity: 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-11-20'
    }
  }
}

// Existing KV — we write the OpenAI API key into it so the App Service can
// resolve it as a Key Vault reference in App Settings.
resource kv 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource secretOpenAIKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: 'AIAzureOpenAIApiKey'
  properties: {
    value: openAI.listKeys().key1
  }
}

// Cognitive Services User role for App Service identities. Lets a future
// migration to managed-identity auth happen without an infra PR.
var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource openAIRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in appPrincipalIds: {
  name: guid(openAI.id, principalId, cognitiveServicesUserRoleId)
  scope: openAI
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

output openAIId string = openAI.id
output openAIEndpoint string = openAI.properties.endpoint
output openAIDeploymentName string = openAIDeployment.name
