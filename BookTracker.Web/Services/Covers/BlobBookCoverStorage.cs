using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace BookTracker.Web.Services.Covers;

// Single Azure.Storage.Blobs implementation used by both prod and local-dev.
// Local connects to Azurite via the well-known dev connection string from
// appsettings.Development.json; prod consumes the real Storage Account
// connection string via Key Vault.
//
// Cache-Control: public, max-age=31536000 is set per blob so browsers cache
// covers for a year — minimises egress and gives near-instant subsequent
// renders. Container creation + public-read access happen on first call so
// app startup doesn't depend on it.
public class BlobBookCoverStorage : IBookCoverStorage
{
    private const string CacheControlHeader = "public, max-age=31536000";

    private readonly CoverStorageOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<BlobBookCoverStorage> _logger;
    private readonly Lazy<Task<BlobContainerClient?>> _containerClient;

    public BlobBookCoverStorage(
        IOptions<CoverStorageOptions> options,
        HttpClient http,
        ILogger<BlobBookCoverStorage> logger)
    {
        _options = options.Value;
        _http = http;
        _logger = logger;
        _containerClient = new Lazy<Task<BlobContainerClient?>>(InitContainerAsync);
    }

    public bool IsEnabled => _options.IsEnabled;

    public bool IsManagedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var publicBase = ResolvePublicBaseUrl();
        return publicBase is not null && url.StartsWith(publicBase, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> MirrorFromUrlAsync(string sourceUrl, string blobKey, CancellationToken ct)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(sourceUrl)) return sourceUrl;
        if (IsManagedUrl(sourceUrl)) return sourceUrl;

        try
        {
            using var response = await _http.GetAsync(sourceUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cover mirror skipped — upstream returned {Status} for {Url}", (int)response.StatusCode, sourceUrl);
                return sourceUrl;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            return await UploadAsync(bytes, contentType ?? "application/octet-stream", blobKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cover mirror failed for {Url}", sourceUrl);
            return sourceUrl;
        }
    }

    public async Task<string> UploadAsync(byte[] bytes, string contentType, string blobKey, CancellationToken ct)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("Cover upload requested but storage is disabled — no connection string configured.");
            throw new InvalidOperationException("Cover storage is not configured.");
        }

        var processed = CoverImageProcessor.Process(bytes, contentType);
        if (!processed.WasNormalised)
        {
            _logger.LogWarning("Cover normalisation failed for blobKey {BlobKey} (content-type {ContentType}, {ByteCount} bytes) — storing raw payload.",
                blobKey, contentType, bytes.Length);
        }

        var container = await _containerClient.Value;
        if (container is null)
        {
            // Container init logged; return original-style fallback to upstream is the caller's job.
            throw new InvalidOperationException("Cover storage container is unavailable.");
        }

        var blobName = $"{blobKey}.{processed.Extension}";
        var blob = container.GetBlobClient(blobName);

        using var ms = new MemoryStream(processed.Bytes);
        await blob.UploadAsync(ms, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = processed.ContentType,
                CacheControl = CacheControlHeader,
            },
        }, ct);

        return BuildPublicUrl(blobName);
    }

    private async Task<BlobContainerClient?> InitContainerAsync()
    {
        try
        {
            var service = new BlobServiceClient(_options.ConnectionString);
            var container = service.GetBlobContainerClient(_options.ContainerName);
            await container.CreateIfNotExistsAsync(PublicAccessType.Blob);
            return container;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialise blob container {Container}.", _options.ContainerName);
            return null;
        }
    }

    private string BuildPublicUrl(string blobName)
    {
        var publicBase = ResolvePublicBaseUrl();
        if (publicBase is not null)
        {
            return publicBase.TrimEnd('/') + "/" + blobName;
        }

        // Last resort — derive from the BlobServiceClient. Slightly more
        // expensive (re-allocates), so kept as a fallback.
        var service = new BlobServiceClient(_options.ConnectionString);
        return service.GetBlobContainerClient(_options.ContainerName).GetBlobClient(blobName).Uri.ToString();
    }

    private string? ResolvePublicBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return _options.PublicBaseUrl;
        }
        if (!_options.IsEnabled) return null;

        try
        {
            var service = new BlobServiceClient(_options.ConnectionString);
            return service.GetBlobContainerClient(_options.ContainerName).Uri.ToString();
        }
        catch
        {
            return null;
        }
    }
}
