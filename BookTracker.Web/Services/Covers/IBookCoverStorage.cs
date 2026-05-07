namespace BookTracker.Web.Services.Covers;

// Mirrors book cover images into Azure Blob Storage so the app's <img src>
// renders never depend on upstream provider latency or availability.
//
// Two write paths today:
// - MirrorFromUrlAsync: fetch an upstream URL (Open Library / Google Books /
//   Trove), convert+resize via CoverImageProcessor, upload as a blob, return
//   the blob URL. Idempotent — re-mirroring an already-managed URL returns
//   it unchanged.
// - UploadAsync: take raw bytes (e.g. user-uploaded photo), process, upload.
//   Future PR2 — declared on the interface now so the contract is stable.
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
    /// Fetch <paramref name="sourceUrl"/>, run it through CoverImageProcessor
    /// (JPEG normalize + resize, fallback to raw bytes when conversion fails),
    /// upload to <c>covers/{<paramref name="blobKey"/>}.{ext}</c>, return the
    /// public URL. On failure (network, invalid response, blob upload error)
    /// returns <paramref name="sourceUrl"/> unchanged so the row keeps its
    /// upstream URL — the caller's record remains usable.
    /// </summary>
    Task<string> MirrorFromUrlAsync(string sourceUrl, string blobKey, CancellationToken ct);

    /// <summary>
    /// Upload raw image bytes (e.g. user-uploaded photo). Same processing
    /// rules as MirrorFromUrlAsync.
    /// </summary>
    Task<string> UploadAsync(byte[] bytes, string contentType, string blobKey, CancellationToken ct);
}
