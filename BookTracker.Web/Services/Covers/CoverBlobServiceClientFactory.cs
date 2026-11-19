using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;

namespace BookTracker.Web.Services.Covers;

// Builds the BlobServiceClient for the configured auth mode. Constructing a
// client makes no network call (the credential fetches a token on the first
// request), so the mode selection is testable without Azure.
internal static class CoverBlobServiceClientFactory
{
    public static BlobServiceClient Create(CoverStorageOptions options) => options.AuthMode switch
    {
        CoverStorageAuthMode.ConnectionString => new BlobServiceClient(options.ConnectionString),
        CoverStorageAuthMode.ManagedIdentity => new BlobServiceClient(new Uri(options.ServiceUri), CreateCredential()),
        _ => throw new InvalidOperationException("Cover storage is not configured."),
    };

    // In App Service DefaultAzureCredential resolves to the slot's
    // system-assigned managed identity; locally it falls back to the
    // developer's Azure CLI / Visual Studio sign-in. It never opens an
    // interactive browser prompt by default.
    internal static TokenCredential CreateCredential() => new DefaultAzureCredential();
}
