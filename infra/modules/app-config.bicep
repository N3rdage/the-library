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

// Cover storage. Connection string is resolved from KV (shared by both
// slots — same storage account). Container name + public URL differ per
// slot so prod and staging writes can't collide on independent Edition IDs;
// both are slot-sticky (see slotConfigNames below).
param coverStorageProdContainerName string = 'book-covers'
param coverStorageStagingContainerName string = 'book-covers-staging'
param coverStorageProdPublicBaseUrl string = ''
param coverStorageStagingPublicBaseUrl string = ''

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
// Shared values across both slots — connection string is one because both
// slots use the same storage account; the container name + public URL differ
// per slot below.
var commonAppSettings = {
  ASPNETCORE_ENVIRONMENT: 'Production'
  APPLICATIONINSIGHTS_CONNECTION_STRING: appInsightsConnectionString
  // App Service Linux warmup probe gives the container 230s by default
  // before declaring failure and entering a kill-and-restart loop.
  // BookTracker's startup adds up to ~250s under realistic conditions:
  // platform `update-ca-certificates` (5-80s, regional), dotnet bootstrap
  // (~15s), first SQL connection with AAD-only auth on Basic tier
  // (~30-40s), EF migration applock + history check (~10s), then app
  // initialisation. 600s gives generous headroom for slow days without
  // letting genuinely broken bits hide forever.
  WEBSITES_CONTAINER_START_TIME_LIMIT: '600'
  // Slot-swap warmup probe — Azure pings this path on the source slot
  // (the one about to be promoted to production) during a swap and
  // won't complete the swap until it returns 2xx. Without it the
  // probe defaults to `/`, which under Easy Auth returns a 302 to
  // login (and warms nothing in the .NET app — Easy Auth blocks
  // before hand-off). `/warmup` is excluded from Easy Auth (see
  // `publicPaths` below) and does a trivial Books.Take(1) so the
  // SQL connection pool + managed-identity AAD token are warm when
  // the slot starts taking real traffic. Implementation:
  // BookTracker.Web/Api/WarmupEndpoints.cs.
  WEBSITE_SWAP_WARMUP_PING_PATH: '/warmup'
  MICROSOFT_PROVIDER_AUTHENTICATION_SECRET: authClientSecretRef
  AI__DefaultProvider: aiDefaultProvider
  AI__AzureOpenAI__Endpoint: aiAzureOpenAIEndpoint
  AI__AzureOpenAI__ApiKey: openAIKeyRef
  AI__AzureOpenAI__Deployment: aiAzureOpenAIDeployment
  AI__Anthropic__ApiKey: anthropicKeyRef
  Trove__ApiKey: troveKeyRef
  CoverStorage__ConnectionString: coverStorageConnRef
}

var prodAppSettingsValues = union(commonAppSettings, {
  CoverStorage__ContainerName: coverStorageProdContainerName
  CoverStorage__PublicBaseUrl: coverStorageProdPublicBaseUrl
})

var stagingAppSettingsValues = union(commonAppSettings, {
  CoverStorage__ContainerName: coverStorageStagingContainerName
  CoverStorage__PublicBaseUrl: coverStorageStagingPublicBaseUrl
})

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
  // ContainerName + PublicBaseUrl MUST be slot-sticky — they differ per slot
  // (`book-covers` vs `book-covers-staging`) so writes don't collide on
  // independent Edition IDs. A swap moving them with the bits would break
  // the just-promoted slot's cover URLs.
  'CoverStorage__ContainerName'
  'CoverStorage__PublicBaseUrl'
]

// Paths served publicly (without Easy Auth). Two distinct shapes:
//
// 1. PWA assets — Chrome/Safari fetch these without credentials during
//    the install-validation handshake. Manifest + icons + service
//    worker script are all non-sensitive (public app name, theme
//    colour, static images, client-side caching logic).
// 2. /warmup — Azure's slot-swap warmup probe (see
//    WEBSITE_SWAP_WARMUP_PING_PATH below) is anonymous; if it required
//    AAD it would 302 to login and never actually warm the .NET app.
//    The endpoint returns a fixed string and reveals no business data,
//    so anonymous access is harmless. Implementation in
//    BookTracker.Web/Api/WarmupEndpoints.cs.
//
// Expand deliberately — every entry here bypasses AAD sign-in.
//
// NOTE: Easy Auth v2 excludedPaths does EXACT path matching, not prefix.
// "/icons" does not match "/icons/icon-192.png" — each file must be listed.
// If you add another icon, add it here too.
var publicPaths = [
  '/manifest.webmanifest'
  '/service-worker.js'
  '/icons/icon.svg'
  '/icons/icon-192.png'
  '/icons/icon-512.png'
  '/icons/apple-touch-icon.png'
  '/warmup'
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
  properties: prodAppSettingsValues
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
      // Min Pool Size=3 keeps a small warm pool so AAD-token + TLS handshake
      // costs don't recur on every idle gap. Without it, sporadic mobile
      // traffic was paying 8-27s connection-open latency (App Insights
      // `InternalOpenAsync` p95) for every fresh physical connection.
      value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Min Pool Size=3;'
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
      excludedPaths: publicPaths
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
  properties: stagingAppSettingsValues
}

resource stagingConnStrings 'Microsoft.Web/sites/slots/config@2023-12-01' = {
  parent: stagingSlot
  name: 'connectionstrings'
  properties: {
    DefaultConnection: {
      value: 'Server=tcp:${sqlServerFqdn},1433;Database=${stagingSqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Min Pool Size=3;'
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
      excludedPaths: publicPaths
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
