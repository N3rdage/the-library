// App Service configuration: app settings, connection strings, Easy Auth.
// Lives in its own module so it can be deployed AFTER Key Vault (so the KV
// references resolve) and AFTER AI services (so AI endpoints are available).

param appServiceName string
param stagingSlotName string
param tenantId string
param authClientId string
param sqlServerFqdn string
param sqlDatabaseName string
param stagingSqlDatabaseName string
param appInsightsConnectionString string

@description('Key Vault name. Used to build @Microsoft.KeyVault references for secret app settings.')
param keyVaultName string

// AI provider config (non-secret values only — secrets resolve via KV refs).
// MicrosoftFoundry settings are intentionally omitted: this subscription is
// Sponsored, so Claude on Foundry isn't deployable; the MicrosoftFoundry
// provider therefore won't appear in the app's picker. See TODO.md for the
// follow-up to add Foundry once on an EA / MCA-E subscription.
param aiAzureOpenAIEndpoint string = ''
param aiAzureOpenAIDeployment string = ''
param aiDefaultProvider string = 'Anthropic'

// Cover storage. Connection string is resolved from KV; container URL is
// non-secret (it's the public read URL embedded in <img src> tags).
param coverStoragePublicBaseUrl string = ''
param coverStorageContainerName string = 'book-covers'

// Key Vault reference helpers. App Service resolves these via its managed
// identity (which has Key Vault Secrets User role) and caches the resolved
// value for the lifetime of the worker. Omitting the secret version tells
// Azure to pick up the latest secret version automatically.
//
// References are ALWAYS emitted, regardless of whether a secret has been
// written to KV yet. An unresolved reference surfaces as an empty string
// to the app (which reads it as "no key configured" — the provider
// silently drops out of the picker). The reason we always emit it: a
// re-deploy without `-AnthropicApiKey` / `-TroveApiKey` used to *remove*
// the app setting entirely, because it was conditional on the deploy
// param. That left the app without any reference to pick up the secret
// on next restart, and a subsequent slot-swap (without the setting on
// both slots) moved the hole around unpredictably.
#disable-next-line no-hardcoded-env-urls
var kvBase = 'https://${keyVaultName}.vault.azure.net/secrets'
var authClientSecretRef = '@Microsoft.KeyVault(SecretUri=${kvBase}/AuthClientSecret/)'
var openAIKeyRef = '@Microsoft.KeyVault(SecretUri=${kvBase}/AIAzureOpenAIApiKey/)'
var anthropicKeyRef = '@Microsoft.KeyVault(SecretUri=${kvBase}/AIAnthropicApiKey/)'
var troveKeyRef = '@Microsoft.KeyVault(SecretUri=${kvBase}/TroveApiKey/)'
var coverStorageConnRef = '@Microsoft.KeyVault(SecretUri=${kvBase}/CoverStorageConnectionString/)'

// Same shape for prod + staging; slotConfigNames below marks the secret-ish
// entries as slot-sticky so swaps don't move them if the two slots' values
// ever diverge.
var appSettingsValues = {
  ASPNETCORE_ENVIRONMENT: 'Production'
  APPLICATIONINSIGHTS_CONNECTION_STRING: appInsightsConnectionString
  MICROSOFT_PROVIDER_AUTHENTICATION_SECRET: authClientSecretRef
  AI__DefaultProvider: aiDefaultProvider
  AI__AzureOpenAI__Endpoint: aiAzureOpenAIEndpoint
  AI__AzureOpenAI__ApiKey: openAIKeyRef
  AI__AzureOpenAI__Deployment: aiAzureOpenAIDeployment
  AI__Anthropic__ApiKey: anthropicKeyRef
  Trove__ApiKey: troveKeyRef
  CoverStorage__ConnectionString: coverStorageConnRef
  CoverStorage__ContainerName: coverStorageContainerName
  CoverStorage__PublicBaseUrl: coverStoragePublicBaseUrl
}

// Settings that must stay pinned to their slot during a swap. Keys and
// environment-flavoured values should never hop between prod and staging,
// even if today they happen to be identical. Azure's slot swap moves any
// setting NOT in this list with the code; listing them here is the only
// way to make them slot-bound.
var slotStickyAppSettingNames = [
  'ASPNETCORE_ENVIRONMENT'
  'MICROSOFT_PROVIDER_AUTHENTICATION_SECRET'
  'AI__Anthropic__ApiKey'
  'AI__AzureOpenAI__ApiKey'
  'AI__AzureOpenAI__Endpoint'
  'AI__AzureOpenAI__Deployment'
  'AI__DefaultProvider'
  'Trove__ApiKey'
  'CoverStorage__ConnectionString'
]

// Paths served publicly (without Easy Auth). Limited to the PWA assets
// Chrome/Safari fetch without credentials during the install-validation
// handshake. Manifest + icons + service worker script are all non-sensitive
// (public app name, theme colour, static images, client-side caching logic).
// Expand deliberately — every entry here bypasses AAD sign-in.
//
// NOTE: Easy Auth v2 excludedPaths does EXACT path matching, not prefix.
// "/icons" does not match "/icons/icon-192.png" — each file must be listed.
// If you add another icon, add it here too.
var pwaPublicPaths = [
  '/manifest.webmanifest'
  '/service-worker.js'
  '/icons/icon.svg'
  '/icons/icon-192.png'
  '/icons/icon-512.png'
  '/icons/apple-touch-icon.png'
]

resource app 'Microsoft.Web/sites@2023-12-01' existing = {
  name: appServiceName
}

resource stagingSlot 'Microsoft.Web/sites/slots@2023-12-01' existing = {
  parent: app
  name: stagingSlotName
}

resource appSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: app
  name: 'appsettings'
  properties: appSettingsValues
}

// Slot-sticky setting names. Attached to the production site (not the slot)
// per Azure's model — a single list governs swap behaviour for all slots.
//
// `DefaultConnection` is slot-sticky too: prod and staging point at separate
// databases (`booktracker` vs `booktracker-staging`). Without slot-stickiness
// a swap would move the connection strings with the bits, which would land
// the formerly-staging code on the prod URL but still pointing at the
// staging DB — an immediate prod outage. Pinning the CS keeps each slot's
// DB stable across swaps; swap is purely code-shaped.
resource slotConfigNames 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: app
  name: 'slotConfigNames'
  properties: {
    appSettingNames: slotStickyAppSettingNames
    connectionStringNames: [
      'DefaultConnection'
    ]
  }
}

resource connStrings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: app
  name: 'connectionstrings'
  properties: {
    DefaultConnection: {
      value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;'
      type: 'SQLAzure'
    }
  }
}

// Easy Auth v2: require sign-in, redirect to AAD. Combined with
// appRoleAssignmentRequired=true on the service principal, only users assigned
// to the "Library-Patrons" enterprise app can complete sign-in.
resource authConfig 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: app
  name: 'authsettingsV2'
  properties: {
    platform: {
      enabled: true
      runtimeVersion: '~2'
    }
    globalValidation: {
      requireAuthentication: true
      unauthenticatedClientAction: 'RedirectToLoginPage'
      redirectToProvider: 'azureactivedirectory'
      excludedPaths: pwaPublicPaths
    }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          openIdIssuer: 'https://sts.windows.net/${tenantId}/v2.0'
          clientId: authClientId
          clientSecretSettingName: 'MICROSOFT_PROVIDER_AUTHENTICATION_SECRET'
        }
        validation: {
          allowedAudiences: [
            'api://${authClientId}'
            authClientId
          ]
        }
      }
    }
    login: {
      tokenStore: {
        enabled: true
      }
    }
  }
  dependsOn: [
    appSettings
  ]
}

resource stagingAppSettings 'Microsoft.Web/sites/slots/config@2023-12-01' = {
  parent: stagingSlot
  name: 'appsettings'
  properties: appSettingsValues
}

resource stagingConnStrings 'Microsoft.Web/sites/slots/config@2023-12-01' = {
  parent: stagingSlot
  name: 'connectionstrings'
  properties: {
    DefaultConnection: {
      value: 'Server=tcp:${sqlServerFqdn},1433;Database=${stagingSqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;'
      type: 'SQLAzure'
    }
  }
}

resource stagingAuthConfig 'Microsoft.Web/sites/slots/config@2023-12-01' = {
  parent: stagingSlot
  name: 'authsettingsV2'
  properties: {
    platform: {
      enabled: true
      runtimeVersion: '~2'
    }
    globalValidation: {
      requireAuthentication: true
      unauthenticatedClientAction: 'RedirectToLoginPage'
      redirectToProvider: 'azureactivedirectory'
      excludedPaths: pwaPublicPaths
    }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          openIdIssuer: 'https://sts.windows.net/${tenantId}/v2.0'
          clientId: authClientId
          clientSecretSettingName: 'MICROSOFT_PROVIDER_AUTHENTICATION_SECRET'
        }
        validation: {
          allowedAudiences: [
            'api://${authClientId}'
            authClientId
          ]
        }
      }
    }
    login: {
      tokenStore: {
        enabled: true
      }
    }
  }
  dependsOn: [
    stagingAppSettings
  ]
}
