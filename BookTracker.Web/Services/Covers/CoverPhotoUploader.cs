namespace BookTracker.Web.Services.Covers;

/// <summary>A cropped cover photo held in memory until the edition it belongs to
/// exists.</summary>
public sealed record CoverPhoto(byte[] Bytes, string ContentType);

public sealed record CoverUploadResult(bool Success, string? ErrorMessage, string? NewUrl)
{
    public static CoverUploadResult Ok(string url) => new(true, null, url);
    public static CoverUploadResult Failure(string error) => new(false, error, null);
}

/// <summary>
/// Validates a user-supplied cover photo and pushes it to cover storage. It owns
/// the size cap, the media-type check and the user-facing messages; persisting the
/// resulting URL is the caller's job (a <c>SetEditionCover</c> command), so this
/// never touches the database.
/// </summary>
public class CoverPhotoUploader(IBookCoverStorage coverStorage, ILogger<CoverPhotoUploader> logger)
{
    /// <summary>Server-side cap on user-uploaded cover photos. 10 MB is generous
    /// enough to accept a 12MP phone-camera JPEG without rejection while
    /// bounding the worst-case payload through Blazor Server's SignalR pipe
    /// and the resize step in CoverImageProcessor.</summary>
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    public bool IsEnabled => coverStorage.IsEnabled;

    public async Task<CoverUploadResult> UploadAsync(byte[] bytes, string contentType, int editionId, CancellationToken ct)
    {
        if (bytes.LongLength > MaxUploadBytes)
        {
            return CoverUploadResult.Failure($"File too large ({bytes.LongLength / 1024 / 1024} MB). Max is {MaxUploadBytes / 1024 / 1024} MB.");
        }

        if (!coverStorage.IsEnabled)
        {
            return CoverUploadResult.Failure("Cover storage is not configured.");
        }

        if (CoverImageProcessor.AllowedMediaType(contentType) is null)
        {
            logger.LogWarning("Cover upload refused for Edition {EditionId}: content type {ContentType} is not an allowed raster image type.",
                editionId, contentType);
            return CoverUploadResult.Failure("That file type isn't supported. Use a JPEG, PNG, GIF or WebP image.");
        }

        try
        {
            var url = await coverStorage.UploadAsync(bytes, contentType, $"editions/{editionId}", ct);
            return CoverUploadResult.Ok(url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cover upload failed for Edition {EditionId}.", editionId);
            return CoverUploadResult.Failure("Upload failed. Try again or check the file.");
        }
    }
}
