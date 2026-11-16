namespace BookTracker.Web.Services.Covers;

public class CoverStorageOptions
{
    public const string SectionName = "CoverStorage";

    /// <summary>
    /// Azure Storage connection string. Use the Azurite well-known dev string locally.
    /// Wins over <see cref="ServiceUri"/> when both are set.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// The account's blob endpoint (<c>https://&lt;account&gt;.blob.core.windows.net/</c>).
    /// Used with the app's managed identity (DefaultAzureCredential) when
    /// <see cref="ConnectionString"/> is empty.
    /// </summary>
    public string ServiceUri { get; set; } = "";

    /// <summary>Blob container name. Container is created on startup if missing.</summary>
    public string ContainerName { get; set; } = "book-covers";

    /// <summary>
    /// Public base URL used to render <img src> tags. Differs from the
    /// account's blob endpoint when the container sits behind a custom domain
    /// or CDN. Empty falls back to the container URL on the blob endpoint.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Which way the blob client authenticates, from the configured values.</summary>
    public CoverStorageAuthMode AuthMode =>
        !string.IsNullOrWhiteSpace(ConnectionString) ? CoverStorageAuthMode.ConnectionString
        : !string.IsNullOrWhiteSpace(ServiceUri) ? CoverStorageAuthMode.ManagedIdentity
        : CoverStorageAuthMode.None;

    /// <summary>True when the service is configured well enough to mirror covers.</summary>
    public bool IsEnabled => AuthMode != CoverStorageAuthMode.None;
}

public enum CoverStorageAuthMode
{
    /// <summary>Neither a connection string nor a service URI: cover storage is off.</summary>
    None,

    /// <summary>Account key from <see cref="CoverStorageOptions.ConnectionString"/>.</summary>
    ConnectionString,

    /// <summary><see cref="CoverStorageOptions.ServiceUri"/> plus DefaultAzureCredential.</summary>
    ManagedIdentity,
}
