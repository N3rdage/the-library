param location string
param tags object
param appServiceName string
param appServicePlanName string
param tenantId string
param authClientId string
@secure()
param authClientSecret string
param sqlServerFqdn string
param sqlDatabaseName string
param appInsightsConnectionString string

// AI provider config — set via Azure Portal or CLI for secrets.
// Only configure the providers you want to use.
@secure()
param aiAnthropicApiKey string = ''
@secure()
param aiAzureFoundryApiKey string = ''
param aiAzureFoundryEndpoint string = ''
param aiAzureFoundryFastDeployment string = ''
param aiAzureFoundryDeepDeployment string = ''
@secure()
param aiAzureOpenAIApiKey string = ''
param aiAzureOpenAIEndpoint string = ''
param aiAzureOpenAIDeployment string = ''
param aiDefaultProvider string = 'Anthropic'

// App Service plan: Linux, S1 (AlwaysOn + slots available, suits Blazor Server).
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'S1'
    tier: 'Standard'
    capacity: 1
  }
  properties: {
    reserved: true
  }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appServiceName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      webSocketsEnabled: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }
}

// App settings. MICROSOFT_PROVIDER_AUTHENTICATION_SECRET is the conventional
// setting name Easy Auth looks up for the AAD client secret.
resource appSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: app
  name: 'appsettings'
  properties: {
    ASPNETCORE_ENVIRONMENT: 'Production'
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsightsConnectionString
    MICROSOFT_PROVIDER_AUTHENTICATION_SECRET: authClientSecret
    AI__DefaultProvider: aiDefaultProvider
    AI__Anthropic__ApiKey: aiAnthropicApiKey
    AI__AzureFoundry__Endpoint: aiAzureFoundryEndpoint
    AI__AzureFoundry__ApiKey: aiAzureFoundryApiKey
    AI__AzureFoundry__FastDeployment: aiAzureFoundryFastDeployment
    AI__AzureFoundry__DeepDeployment: aiAzureFoundryDeepDeployment
    AI__AzureOpenAI__Endpoint: aiAzureOpenAIEndpoint
    AI__AzureOpenAI__ApiKey: aiAzureOpenAIApiKey
    AI__AzureOpenAI__Deployment: aiAzureOpenAIDeployment
  }
}

// AAD-only SQL: Active Directory Default uses the system-assigned managed
// identity when running in App Service. No password in the connection string.
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

// Staging deployment slot — GitHub Actions deploys here; swap.yml promotes to prod.
// Each slot gets its own system-assigned managed identity, so both need to be
// granted on the SQL DB (deploy.ps1 handles both).
resource stagingSlot 'Microsoft.Web/sites/slots@2023-12-01' = {
  parent: app
  name: 'staging'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      webSocketsEnabled: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }
}

resource stagingAppSettings 'Microsoft.Web/sites/slots/config@2023-12-01' = {
  parent: stagingSlot
  name: 'appsettings'
  properties: {
    ASPNETCORE_ENVIRONMENT: 'Production'
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsightsConnectionString
    MICROSOFT_PROVIDER_AUTHENTICATION_SECRET: authClientSecret
    AI__DefaultProvider: aiDefaultProvider
    AI__Anthropic__ApiKey: aiAnthropicApiKey
    AI__AzureFoundry__Endpoint: aiAzureFoundryEndpoint
    AI__AzureFoundry__ApiKey: aiAzureFoundryApiKey
    AI__AzureFoundry__FastDeployment: aiAzureFoundryFastDeployment
    AI__AzureFoundry__DeepDeployment: aiAzureFoundryDeepDeployment
    AI__AzureOpenAI__Endpoint: aiAzureOpenAIEndpoint
    AI__AzureOpenAI__ApiKey: aiAzureOpenAIApiKey
    AI__AzureOpenAI__Deployment: aiAzureOpenAIDeployment
  }
}

resource stagingConnStrings 'Microsoft.Web/sites/slots/config@2023-12-01' = {
  parent: stagingSlot
  name: 'connectionstrings'
  properties: {
    DefaultConnection: {
      value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;'
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

output appServiceUrl string = 'https://${app.properties.defaultHostName}'
output appServiceName string = app.name
output defaultHostName string = app.properties.defaultHostName
output customDomainVerificationId string = app.properties.customDomainVerificationId
output principalId string = app.identity.principalId
output stagingHostName string = stagingSlot.properties.defaultHostName
output stagingPrincipalId string = stagingSlot.identity.principalId
