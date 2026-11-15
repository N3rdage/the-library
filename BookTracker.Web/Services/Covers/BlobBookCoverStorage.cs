using System.Net;
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

    /// <summary>Largest upstream cover body accepted for mirroring (10 MiB).</summary>
    public const long MaxDownloadBytes = 10 * 1024 * 1024;

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

    // Test seam: supplies the blob container directly instead of building it
    // from the connection string.
    internal BlobBookCoverStorage(
        IOptions<CoverStorageOptions> options,
        HttpClient http,
        ILogger<BlobBookCoverStorage> logger,
        BlobContainerClient container)
    {
        _options = options.Value;
        _http = http;
        _logger = logger;
        _containerClient = new Lazy<Task<BlobContainerClient?>>(() => Task.FromResult<BlobContainerClient?>(container));
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

        var refusal = CoverFetchPolicy.Validate(sourceUrl);
        if (refusal is not null)
        {
            _logger.LogWarning("Cover mirror skipped — {Url} refused: {Reason}.", sourceUrl, refusal);
            return sourceUrl;
        }

        // ResponseHeadersRead means HttpClient.Timeout covers only the status
        // line and headers; the body read has to be bounded separately or a
        // slow-drip upstream stalls the sequential mirror loop. The linked
        // source bounds the whole download (headers plus body) by the same
        // client timeout the download had before.
        using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        downloadTimeout.CancelAfter(_http.Timeout);

        try
        {
            using var response = await GetFollowingRedirectsAsync(sourceUrl, downloadTimeout.Token);
            if (response is null) return sourceUrl;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cover mirror skipped — upstream returned {Status} for {Url}", (int)response.StatusCode, sourceUrl);
                return sourceUrl;
            }

            var declaredType = response.Content.Headers.ContentType?.MediaType;
            var mediaType = CoverImageProcessor.AllowedMediaType(declaredType);
            if (mediaType is null)
            {
                _logger.LogWarning("Cover mirror skipped — {Url} returned content type {MediaType}, not an allowed raster image type.",
                    sourceUrl, declaredType ?? "(none)");
                return sourceUrl;
            }

            if (response.Content.Headers.ContentLength > MaxDownloadBytes)
            {
                _logger.LogWarning("Cover mirror skipped — {Url} declares {ByteCount} bytes, over the {MaxBytes}-byte cap.",
                    sourceUrl, response.Content.Headers.ContentLength, MaxDownloadBytes);
                return sourceUrl;
            }

            var bytes = await ReadCappedAsync(response.Content, downloadTimeout.Token);
            if (bytes is null)
            {
                _logger.LogWarning("Cover mirror skipped — {Url} body exceeded the {MaxBytes}-byte cap while reading.",
                    sourceUrl, MaxDownloadBytes);
                return sourceUrl;
            }

            var processed = CoverImageProcessor.Process(bytes, mediaType);
            if (processed is null)
            {
                _logger.LogWarning("Cover mirror skipped — {Url} could not be decoded as {MediaType}; the row keeps its upstream URL.",
                    sourceUrl, mediaType);
                return sourceUrl;
            }

            return await StoreAsync(processed, blobKey, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Cover mirror skipped — {Url} did not finish downloading within {Timeout}.",
                sourceUrl, _http.Timeout);
            return sourceUrl;
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
        if (processed is null)
        {
            _logger.LogWarning("Cover upload refused for blobKey {BlobKey} — content type {ContentType} ({ByteCount} bytes) is not an allowed raster image type or could not be decoded.",
                blobKey, contentType, bytes.Length);
            throw new InvalidOperationException("Cover is not an allowed image type or could not be decoded.");
        }

        return await StoreAsync(processed, blobKey, ct);
    }

    // The single blob-upload path for both mirrored and uploaded covers.
    private async Task<string> StoreAsync(CoverImageProcessor.ProcessedImage processed, string blobKey, CancellationToken ct)
    {
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

        // Cache-bust the year-long Cache-Control header by appending a unix
        // timestamp. Re-uploads to the same blob key produce a new URL each
        // time, forcing browsers to refetch — without this the stale image
        // can hang around in cache for up to a year.
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"{BuildPublicUrl(blobName)}?v={stamp}";
    }

    // The handler does not follow redirects, so they are followed here: at most
    // CoverFetchPolicy.MaxRedirects hops, each target re-checked by the policy.
    // Returns the first non-redirect response (a redirect without a Location
    // header counts as one), or null after logging when a hop is refused or the
    // chain is too long.
    private async Task<HttpResponseMessage?> GetFollowingRedirectsAsync(string sourceUrl, CancellationToken ct)
    {
        var currentUri = new Uri(sourceUrl);
        for (var redirects = 0; ; redirects++)
        {
            var response = await _http.GetAsync(currentUri, HttpCompletionOption.ResponseHeadersRead, ct);
            var location = response.Headers.Location;
            if (!IsRedirect(response.StatusCode) || location is null) return response;
            response.Dispose();

            if (redirects == CoverFetchPolicy.MaxRedirects)
            {
                _logger.LogWarning("Cover mirror skipped — {Url} redirected more than {MaxRedirects} times.",
                    sourceUrl, CoverFetchPolicy.MaxRedirects);
                return null;
            }

            var nextUri = new Uri(currentUri, location);
            var refusal = CoverFetchPolicy.Validate(nextUri.AbsoluteUri);
            if (refusal is not null)
            {
                _logger.LogWarning("Cover mirror skipped — {Url} redirected to {Target}, refused: {Reason}.",
                    sourceUrl, nextUri.AbsoluteUri, refusal);
                return null;
            }

            currentUri = nextUri;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    // Reads at most MaxDownloadBytes from the body; returns null as soon as the
    // stream goes past the cap. Content-Length can be absent or wrong, so the
    // cap is enforced on the bytes actually read.
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxDownloadBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
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
