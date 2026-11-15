using SkiaSharp;

namespace BookTracker.Web.Services.Covers;

// Normalises cover images to a single shape (JPEG, max 1200px on the long
// edge) before they hit blob storage. Only the raster types in
// AllowedRawExtensions are accepted; anything else is refused outright. An
// allowed type that fails to decode falls back to "store the bytes raw" —
// the upload still succeeds and the browser will render whatever it can.
// Per Drew's call: log a warning on conversion failure and let mis-rendered
// images surface as bugs to address manually.
//
// Uses SkiaSharp — the same imaging library as the Bookshelf cover cache
// (BookTracker.Mobile.Cache) — replacing SixLabors.ImageSharp, which moved to
// a build-time licence key in v4. Decode/resize/encode mirrors
// CatalogCache.ResizeToJpeg.
public static class CoverImageProcessor
{
    /// <summary>Long-edge max in pixels. 1200 is plenty for retina displays of a cover thumbnail.</summary>
    public const int MaxEdgePixels = 1200;
    public const int JpegQuality = 85;
    public const string NormalisedContentType = "image/jpeg";
    public const string NormalisedExtension = "jpg";

    /// <summary>Raster media types accepted as cover input, mapped to the blob
    /// extension used when the raw bytes have to be stored un-normalised.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedRawExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = "jpg",
            ["image/png"] = "png",
            ["image/gif"] = "gif",
            ["image/webp"] = "webp",
        };

    public record ProcessedImage(byte[] Bytes, string ContentType, string Extension, bool WasNormalised);

    /// <summary>
    /// Returns the lower-cased media type of <paramref name="contentType"/>
    /// (parameters such as "; charset=..." stripped) when it is in
    /// <see cref="AllowedRawExtensions"/>; otherwise null. The common
    /// non-standard spelling <c>image/jpg</c> is accepted as <c>image/jpeg</c>.
    /// </summary>
    public static string? AllowedMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;

        var mediaType = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (mediaType == "image/jpg") mediaType = "image/jpeg";
        return AllowedRawExtensions.ContainsKey(mediaType) ? mediaType : null;
    }

    /// <summary>
    /// Decodes the input, resizes to the long-edge cap if larger, re-encodes as
    /// JPEG. Returns the normalised payload on success.
    /// Returns null when <paramref name="sourceContentType"/> is not an allowed
    /// raster type. On decode failure of an allowed type (corrupt bytes etc.),
    /// returns the original bytes with that type's content type / extension +
    /// <see cref="ProcessedImage.WasNormalised"/>=false.
    /// Caller is expected to log the failure path so unrenderable images are visible.
    /// </summary>
    public static ProcessedImage? Process(byte[] inputBytes, string? sourceContentType)
    {
        var mediaType = AllowedMediaType(sourceContentType);
        if (mediaType is null) return null;

        try
        {
            var normalised = TryNormalise(inputBytes);
            if (normalised is not null)
            {
                return new ProcessedImage(normalised, NormalisedContentType, NormalisedExtension, WasNormalised: true);
            }
        }
        catch
        {
            // Fall through to the raw-bytes path. SkiaSharp can throw (e.g.
            // ArgumentNullException on null bytes) as well as return null;
            // both mean "couldn't normalise".
        }

        // Couldn't decode/encode — return the raw bytes so the upload still
        // happens, labelled with the allowed source type.
        return new ProcessedImage(inputBytes, mediaType, AllowedRawExtensions[mediaType], WasNormalised: false);
    }

    // Decode -> (resize if over the long-edge cap) -> JPEG encode. Returns null
    // when the bytes aren't a decodable image (SKBitmap.Decode returns null).
    // SkiaSharp's Resize takes SKSamplingOptions (the SKFilterQuality
    // replacement); Mitchell cubic is a good downscale fit for cover art and
    // matches the Mobile cover cache.
    private static byte[]? TryNormalise(byte[] inputBytes)
    {
        using var source = SKBitmap.Decode(inputBytes);
        if (source is null) return null;

        var longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= MaxEdgePixels)
        {
            // Under the cap — re-encode the decoded bitmap to JPEG so the format
            // is still normalised (the old ImageSharp path did the same).
            return EncodeJpeg(source);
        }

        var scale = (double)MaxEdgePixels / longEdge;
        // Floor each edge at 1px so an extreme aspect ratio (e.g. 3000x1, where the
        // short edge rounds to 0) can't produce a zero-dimension SKImageInfo —
        // Resize returns null on that and we'd silently store the raw un-normalised
        // bytes. Mirrors CatalogCache.ScaleToLongEdge.
        var newWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var newHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var resized = source.Resize(
            new SKImageInfo(newWidth, newHeight, source.ColorType, source.AlphaType),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (resized is null) return null;

        return EncodeJpeg(resized);
    }

    private static byte[]? EncodeJpeg(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return encoded?.ToArray();
    }
}
