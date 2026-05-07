// Storage Account + book-covers blob container for the cover-mirroring
// service. Storage is Standard_LRS — cheapest, single-region durability
// fine for non-critical mirrored content (the upstream URL is the source
// of truth; if a region goes hard-offline we re-mirror from upstream on
// recovery). Container is public-read so <img src> tags work without
// SAS-token complexity; the URLs are non-sensitive (cover thumbnails are
// public-domain content).
//
// Connection string lands in Key Vault as CoverStorageConnectionString;
// app-config.bicep resolves it via a KV reference for both prod and
// staging slots. App-side identity-based auth is a possible follow-up.
param location string
param tags object
param storageAccountName string
param keyVaultName string

// Two containers — one per slot — share a single storage account. Mirrors
// the slot-isolation pattern used for SQL (one server, two databases). Keys
// like `editions/{id}.jpg` would collide across slots if both wrote to the
// same container, since prod and staging DBs assign IDs independently.
param prodContainerName string = 'book-covers'
param stagingContainerName string = 'book-covers-staging'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    // Allow public-read blob URLs at the container level. Account-level
    // anonymous access is permitted so the container's public-read setting
    // takes effect; no anonymous WRITE is ever exposed (writes require the
    // account key from the connection string).
    allowBlobPublicAccess: true
    accessTier: 'Hot'
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    cors: {
      corsRules: []
    }
  }
}

resource prodContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: prodContainerName
  properties: {
    // Blob-level public read — anyone with the URL can GET, no listing.
    publicAccess: 'Blob'
  }
}

resource stagingContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: stagingContainerName
  properties: {
    publicAccess: 'Blob'
  }
}

// Build the connection string from the account's primary key and write it
// to KV. Pattern matches ai-services.bicep — secrets that derive from
// resources we provision here stay out of deployment params.
resource kv 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

var primaryKey = storageAccount.listKeys().keys[0].value
var connectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${primaryKey};EndpointSuffix=${environment().suffixes.storage}'

resource secretConnectionString 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: 'CoverStorageConnectionString'
  properties: {
    value: connectionString
  }
}

output storageAccountName string = storageAccount.name
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
output prodContainerName string = prodContainerName
output stagingContainerName string = stagingContainerName
output prodPublicContainerUrl string = '${storageAccount.properties.primaryEndpoints.blob}${prodContainerName}'
output stagingPublicContainerUrl string = '${storageAccount.properties.primaryEndpoints.blob}${stagingContainerName}'
