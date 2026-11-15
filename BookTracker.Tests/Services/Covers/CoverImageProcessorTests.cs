using BookTracker.Web.Services.Covers;
using SkiaSharp;

namespace BookTracker.Tests.Services.Covers;

[Trait("Category", TestCategories.Unit)]
public class CoverImageProcessorTests
{
    [Fact]
    public void Process_NormalisesPngToJpeg()
    {
        var pngBytes = MakePng(width: 200, height: 300);

        var result = CoverImageProcessor.Process(pngBytes, sourceContentType: "image/png");

        Assert.NotNull(result);
        Assert.Equal(CoverImageProcessor.NormalisedContentType, result.ContentType);
        Assert.Equal(CoverImageProcessor.NormalisedExtension, result.Extension);

        // Re-decode the output to confirm dimensions preserved (no resize since under cap).
        var (width, height) = Decode(result.Bytes);
        Assert.Equal(200, width);
        Assert.Equal(300, height);
    }

    [Fact]
    public void Process_ResizesToLongEdgeCap_WhenInputExceeds()
    {
        // 2000×1000 source — long edge 2000, cap is 1200.
        var jpegBytes = MakeJpeg(width: 2000, height: 1000);

        var result = CoverImageProcessor.Process(jpegBytes, sourceContentType: "image/jpeg");

        Assert.NotNull(result);
        var (width, height) = Decode(result.Bytes);
        Assert.Equal(CoverImageProcessor.MaxEdgePixels, width);
        // 1200 / 2000 = 0.6; 1000 * 0.6 = 600.
        Assert.Equal(600, height);
    }

    [Fact]
    public void Process_PreservesAspectRatioOnResize_TallerThanWide()
    {
        // 1000×3000 source — long edge 3000, cap is 1200.
        var jpegBytes = MakeJpeg(width: 1000, height: 3000);

        var result = CoverImageProcessor.Process(jpegBytes, sourceContentType: "image/jpeg");

        Assert.NotNull(result);
        var (width, height) = Decode(result.Bytes);
        Assert.Equal(CoverImageProcessor.MaxEdgePixels, height);
        // 1200 / 3000 = 0.4; 1000 * 0.4 = 400.
        Assert.Equal(400, width);
    }

    [Fact]
    public void Process_ExtremeAspectRatio_ClampsShortEdgeToOne()
    {
        // 3000×1 source — long edge 3000 > cap. Short edge scales to 1200/3000 = 0.4,
        // which rounds to 0 without the Math.Max(1, …) floor; a zero-dimension
        // SKImageInfo makes Resize return null and the cover would be skipped.
        var jpegBytes = MakeJpeg(width: 3000, height: 1);

        var result = CoverImageProcessor.Process(jpegBytes, sourceContentType: "image/jpeg");

        Assert.NotNull(result);
        var (width, height) = Decode(result.Bytes);
        Assert.Equal(CoverImageProcessor.MaxEdgePixels, width);
        Assert.Equal(1, height); // floored, not 0
    }

    [Fact]
    public void Process_LeavesSmallImagesAlone()
    {
        // 400×600 — under the 1200 cap, no resize.
        var jpegBytes = MakeJpeg(width: 400, height: 600);

        var result = CoverImageProcessor.Process(jpegBytes, sourceContentType: "image/jpeg");

        Assert.NotNull(result);
        var (width, height) = Decode(result.Bytes);
        Assert.Equal(400, width);
        Assert.Equal(600, height);
    }

    [Fact]
    public void Process_CorruptBytes_ReturnsNull()
    {
        // Random bytes — SkiaSharp can't decode, so there is nothing to store.
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };

        var result = CoverImageProcessor.Process(garbage, sourceContentType: "image/png");

        Assert.Null(result);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/jpg")]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    [InlineData("IMAGE/PNG")]
    [InlineData("image/jpeg; charset=binary")]
    public void Process_AllowedTypeWithUndecodableBytes_ReturnsNull(string sourceHint)
    {
        var garbage = new byte[] { 0xFF, 0xD8 }; // truncated JPEG header — fails decode

        var result = CoverImageProcessor.Process(garbage, sourceContentType: sourceHint);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("image/jpeg", "image/jpeg")]
    [InlineData("image/jpg", "image/jpeg")] // alt spelling normalised
    [InlineData("image/png", "image/png")]
    [InlineData("image/gif", "image/gif")]
    [InlineData("image/webp", "image/webp")]
    [InlineData("IMAGE/PNG", "image/png")] // case-insensitive
    [InlineData("image/jpeg; charset=binary", "image/jpeg")] // strips charset suffix
    public void AllowedMediaType_RecognisesAllowedTypes(string contentType, string expected)
    {
        Assert.Equal(expected, CoverImageProcessor.AllowedMediaType(contentType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    [InlineData("image/heic")]
    [InlineData("application/pdf")]
    public void Process_RefusesNonAllowlistedContentType(string? hint)
    {
        // A decodable image is still refused when the declared type isn't allowed.
        var result = CoverImageProcessor.Process(MakePng(width: 10, height: 10), sourceContentType: hint);

        Assert.Null(result);
    }

    private static byte[] MakePng(int width, int height) => MakeImage(width, height, SKEncodedImageFormat.Png);

    private static byte[] MakeJpeg(int width, int height) => MakeImage(width, height, SKEncodedImageFormat.Jpeg);

    private static byte[] MakeImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.SlateGray); // non-empty content so encoders have something to write
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 85);
        Assert.NotNull(data); // clear failure if the encoder ever returns null, not an opaque NRE
        return data.ToArray();
    }

    private static (int Width, int Height) Decode(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        return (bitmap!.Width, bitmap.Height);
    }
}
