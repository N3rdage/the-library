using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace BookTracker.Web.Services.Covers;

// Normalises cover images to a single shape (JPEG, max 1200px on the long
// edge) before they hit blob storage. Unsupported / corrupt input falls back
// to "store the bytes raw" — the upload still succeeds and the browser will
// render whatever it can. Per Drew's call: log a warning on conversion
// failure and let mis-rendered images surface as bugs to address manually.
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
    /// derived from <paramref name="sourceContentType"/> + <see cref="WasNormalised"/>=false.
    /// Caller is expected to log the failure path so unrenderable images are visible.
    /// </summary>
    public static ProcessedImage Process(byte[] inputBytes, string? sourceContentType)
    {
        try
        {
            using var image = Image.Load(inputBytes);

            var longEdge = Math.Max(image.Width, image.Height);
            if (longEdge > MaxEdgePixels)
            {
                var scale = (double)MaxEdgePixels / longEdge;
                var newWidth = (int)Math.Round(image.Width * scale);
                var newHeight = (int)Math.Round(image.Height * scale);
                image.Mutate(x => x.Resize(newWidth, newHeight));
            }

            using var output = new MemoryStream();
            var encoder = new JpegEncoder { Quality = JpegQuality };
            image.Save(output, encoder);
            return new ProcessedImage(output.ToArray(), NormalisedContentType, NormalisedExtension, WasNormalised: true);
        }
        catch
        {
            // Caller logs; we just return the raw bytes so the upload still
            // happens. Pick the closest content-type / extension we can
            // derive — falls back to octet-stream if the source didn't
            // declare one.
            var (contentType, extension) = InferRawShape(sourceContentType);
            return new ProcessedImage(inputBytes, contentType, extension, WasNormalised: false);
        }
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
