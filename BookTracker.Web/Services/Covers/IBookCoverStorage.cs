namespace BookTracker.Web.Services.Covers;

// Mirrors book cover images into Azure Blob Storage so the app's <img src>
// renders never depend on upstream provider latency or availability.
//
// Two write paths:
// - MirrorFromUrlAsync: fetch an upstream URL (Open Library / Google Books /
//   Trove), convert+resize via CoverImageProcessor, upload as a blob, and
//   report a classified CoverMirrorResult. Idempotent — an already-managed
//   URL comes back unchanged as AlreadyManaged.
// - UploadAsync: take raw bytes (e.g. user-uploaded photo), process, upload.
//
// IsManagedUrl tells callers whether a URL is already pointing at our blob,
// so the background mirror service can skip rows that don't need processing.
public interface IBookCoverStorage
{
    /// <summary>True when the storage backend is configured (connection string present).</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// True if the URL points at our blob container — used to decide whether a
    /// row still needs mirroring. Cheap, no I/O.
    /// </summary>
    bool IsManagedUrl(string? url);

    /// <summary>
    /// Fetch <paramref name="sourceUrl"/> (only an allowed raster content type,
    /// at most <see cref="BlobBookCoverStorage.MaxDownloadBytes"/>, within the
    /// client timeout), run it through CoverImageProcessor (JPEG normalize +
    /// resize), upload to <c>covers/{<paramref name="blobKey"/>}.jpg</c>.
    /// The result's <see cref="CoverMirrorResult.Url"/> is the public blob URL
    /// when the status is <see cref="CoverMirrorStatus.Mirrored"/>, otherwise
    /// <paramref name="sourceUrl"/> unchanged so the row keeps its upstream URL.
    /// The status is <see cref="CoverMirrorStatus.AlreadyManaged"/> when there is
    /// nothing to do (already a blob URL, blank, or storage disabled);
    /// <see cref="CoverMirrorStatus.PermanentRefusal"/> when the URL, a redirect
    /// target, the redirect count, a 4xx status (other than 408 and 429), the
    /// content type, the size or the image decode is refused; and
    /// <see cref="CoverMirrorStatus.TransientFailure"/> for 408, 429, 5xx and
    /// other non-success statuses, the download timeout, and any exception from
    /// the HTTP call or the blob upload.
    /// </summary>
    Task<CoverMirrorResult> MirrorFromUrlAsync(string sourceUrl, string blobKey, CancellationToken ct);

    /// <summary>
    /// Upload image bytes (e.g. user-uploaded photo). Same processing rules as
    /// MirrorFromUrlAsync; throws <see cref="InvalidOperationException"/> when
    /// the content type is not allowed or the image cannot be decoded.
    /// </summary>
    Task<string> UploadAsync(byte[] bytes, string contentType, string blobKey, CancellationToken ct);
}
