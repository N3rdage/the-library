using SkiaSharp;

namespace BookTracker.Web.Services.Covers;

// Normalises cover images to a single shape (JPEG, max 1200px on the long
// edge) before they hit blob storage. Unsupported / corrupt input falls back
// to "store the bytes raw" — the upload still succeeds and the browser will
// render whatever it can. Per Drew's call: log a warning on conversion
// failure and let mis-rendered images surface as bugs to address manually.
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

    public record ProcessedImage(byte[] Bytes, string ContentType, string Extension, bool WasNormalised);

    /// <summary>
    /// Decodes the input, resizes to the long-edge cap if larger, re-encodes as
    /// JPEG. Returns the normalised payload on success.
    /// On failure (HEIC without codec, corrupt bytes, animated formats etc.),
    /// returns the original bytes with a best-effort content type / extension
    /// derived from <paramref name="sourceContentType"/> + <see cref="ProcessedImage.WasNormalised"/>=false.
    /// Caller is expected to log the failure path so unrenderable images are visible.
    /// </summary>
    public static ProcessedImage Process(byte[] inputBytes, string? sourceContentType)
    {
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
        // happens. Pick the closest content-type / extension we can derive —
        // falls back to octet-stream if the source didn't declare one.
        var (contentType, extension) = InferRawShape(sourceContentType);
        return new ProcessedImage(inputBytes, contentType, extension, WasNormalised: false);
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
        var newWidth = (int)Math.Round(source.Width * scale);
        var newHeight = (int)Math.Round(source.Height * scale);
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

    private static (string ContentType, string Extension) InferRawShape(string? sourceContentType)
    {
        if (string.IsNullOrWhiteSpace(sourceContentType))
        {
            return ("application/octet-stream", "bin");
        }

        var lower = sourceContentType.Trim().ToLowerInvariant();
        // Strip any "; charset=..." suffix.
        var semi = lower.IndexOf(';');
        if (semi > 0) lower = lower[..semi].Trim();

        return lower switch
        {
            "image/jpeg" or "image/jpg" => ("image/jpeg", "jpg"),
            "image/png" => ("image/png", "png"),
            "image/gif" => ("image/gif", "gif"),
            "image/webp" => ("image/webp", "webp"),
            "image/heic" or "image/heif" => ("image/heic", "heic"),
            "image/avif" => ("image/avif", "avif"),
            "image/svg+xml" => ("image/svg+xml", "svg"),
            _ => ("application/octet-stream", "bin"),
        };
    }
}
