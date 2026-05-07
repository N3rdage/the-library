namespace BookTracker.Web.Services.Covers;

public class CoverStorageOptions
{
    public const string SectionName = "CoverStorage";

    /// <summary>Azure Storage connection string. Use the Azurite well-known dev string locally.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Blob container name. Container is created on startup if missing.</summary>
    public string ContainerName { get; set; } = "book-covers";

    /// <summary>
    /// Public base URL used to render <img src> tags. Differs from the connection
    /// string's blob endpoint when the container sits behind a custom domain or
    /// CDN. Empty falls back to the blob endpoint from the connection string.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>True when the service is configured well enough to mirror covers.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(ConnectionString);
}
