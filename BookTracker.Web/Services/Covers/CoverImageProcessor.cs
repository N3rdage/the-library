using SkiaSharp;

namespace BookTracker.Web.Services.Covers;

// Normalises cover images to a single shape (JPEG, max 1200px on the long
// edge) before they hit blob storage. Only the raster types in
// AllowedMediaTypes are accepted; anything else is refused outright. An
// allowed type that SkiaSharp cannot decode is refused too, so every stored
// cover is a normalised JPEG. Callers log a refusal and skip the upload.
//
// Uses SkiaSharp, the same imaging library as the Bookshelf cover cache
// (BookTracker.Mobile.Cache). Decode/resize/encode mirrors
// CatalogCache.ResizeToJpeg.
public static class CoverImageProcessor
{
    /// <summary>Long-edge max in pixels. 1200 is plenty for retina displays of a cover thumbnail.</summary>
    public const int MaxEdgePixels = 1200;
    public const int JpegQuality = 85;
    public const string NormalisedContentType = "image/jpeg";
    public const string NormalisedExtension = "jpg";

    /// <summary>Raster media types accepted as cover input.</summary>
    public static readonly IReadOnlySet<string> AllowedMediaTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/gif",
            "image/webp",
        };

    public record ProcessedImage(byte[] Bytes, string ContentType, string Extension);

    /// <summary>
    /// Returns the lower-cased media type of <paramref name="contentType"/>
    /// (parameters such as "; charset=..." stripped) when it is in
    /// <see cref="AllowedMediaTypes"/>; otherwise null. The common
    /// non-standard spelling <c>image/jpg</c> is accepted as <c>image/jpeg</c>.
    /// </summary>
    public static string? AllowedMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;

        var mediaType = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (mediaType == "image/jpg") mediaType = "image/jpeg";
        return AllowedMediaTypes.Contains(mediaType) ? mediaType : null;
    }

    /// <summary>
    /// Decodes the input, resizes to the long-edge cap if larger, re-encodes as
    /// JPEG, and returns the normalised payload.
    /// Returns null when <paramref name="sourceContentType"/> is not an allowed
    /// raster type, or when the bytes cannot be decoded or encoded (corrupt
    /// bytes etc.). The caller logs the null result and skips the upload.
    /// </summary>
    public static ProcessedImage? Process(byte[] inputBytes, string? sourceContentType)
    {
        if (AllowedMediaType(sourceContentType) is null) return null;

        try
        {
            var normalised = TryNormalise(inputBytes);
            return normalised is null
                ? null
                : new ProcessedImage(normalised, NormalisedContentType, NormalisedExtension);
        }
        catch
        {
            // SkiaSharp can throw (e.g. ArgumentNullException on null bytes) as
            // well as return null; both mean the image cannot be normalised.
            return null;
        }
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
            // is still normalised.
            return EncodeJpeg(source);
        }

        var scale = (double)MaxEdgePixels / longEdge;
        // Floor each edge at 1px so an extreme aspect ratio (e.g. 3000x1, where the
        // short edge rounds to 0) can't produce a zero-dimension SKImageInfo —
        // Resize returns null on that and the cover would be skipped.
        // Mirrors CatalogCache.ScaleToLongEdge.
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
