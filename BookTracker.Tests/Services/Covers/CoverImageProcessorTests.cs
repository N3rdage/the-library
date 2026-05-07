using BookTracker.Web.Services.Covers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace BookTracker.Tests.Services.Covers;

[Trait("Category", TestCategories.Unit)]
public class CoverImageProcessorTests
{
    [Fact]
    public void Process_NormalisesPngToJpeg()
    {
        var pngBytes = MakePng(width: 200, height: 300);

        var result = CoverImageProcessor.Process(pngBytes, sourceContentType: "image/png");

        Assert.True(result.WasNormalised);
        Assert.Equal(CoverImageProcessor.NormalisedContentType, result.ContentType);
        Assert.Equal(CoverImageProcessor.NormalisedExtension, result.Extension);

        // Re-decode the output to confirm dimensions preserved (no resize since under cap).
        using var output = Image.Load(result.Bytes);
        Assert.Equal(200, output.Width);
        Assert.Equal(300, output.Height);
    }

    [Fact]
    public void Process_ResizesToLongEdgeCap_WhenInputExceeds()
    {
        // 2000×1000 source — long edge 2000, cap is 1200.
        var jpegBytes = MakeJpeg(width: 2000, height: 1000);

        var result = CoverImageProcessor.Process(jpegBytes, sourceContentType: "image/jpeg");

        Assert.True(result.WasNormalised);
        using var output = Image.Load(result.Bytes);
        Assert.Equal(CoverImageProcessor.MaxEdgePixels, output.Width);
        // 1200 / 2000 = 0.6; 1000 * 0.6 = 600.
        Assert.Equal(600, output.Height);
    }

    [Fact]
    public void Process_PreservesAspectRatioOnResize_TallerThanWide()
    {
        // 1000×3000 source — long edge 3000, cap is 1200.
        var jpegBytes = MakeJpeg(width: 1000, height: 3000);

        var result = CoverImageProcessor.Process(jpegBytes, sourceContentType: "image/jpeg");

        using var output = Image.Load(result.Bytes);
        Assert.Equal(CoverImageProcessor.MaxEdgePixels, output.Height);
        // 1200 / 3000 = 0.4; 1000 * 0.4 = 400.
        Assert.Equal(400, output.Width);
    }

    [Fact]
    public void Process_LeavesSmallImagesAlone()
    {
        // 400×600 — under the 1200 cap, no resize.
        var jpegBytes = MakeJpeg(width: 400, height: 600);

        var result = CoverImageProcessor.Process(jpegBytes, sourceContentType: "image/jpeg");

        using var output = Image.Load(result.Bytes);
        Assert.Equal(400, output.Width);
        Assert.Equal(600, output.Height);
    }

    [Fact]
    public void Process_CorruptBytes_FallsBackToRaw_WithRecognisedContentType()
    {
        // Random bytes — ImageSharp can't decode. Should return WasNormalised=false
        // with the bytes preserved and a sensible content-type derived from the
        // hint we passed in.
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };

        var result = CoverImageProcessor.Process(garbage, sourceContentType: "image/heic");

        Assert.False(result.WasNormalised);
        Assert.Equal(garbage, result.Bytes);
        Assert.Equal("image/heic", result.ContentType);
        Assert.Equal("heic", result.Extension);
    }

    [Theory]
    [InlineData("image/jpeg", "image/jpeg", "jpg")]
    [InlineData("image/jpg", "image/jpeg", "jpg")] // alt spelling normalised
    [InlineData("image/png", "image/png", "png")]
    [InlineData("image/gif", "image/gif", "gif")]
    [InlineData("image/webp", "image/webp", "webp")]
    [InlineData("image/heic", "image/heic", "heic")]
    [InlineData("image/heif", "image/heic", "heic")]
    [InlineData("image/avif", "image/avif", "avif")]
    [InlineData("image/svg+xml", "image/svg+xml", "svg")]
    [InlineData("image/jpeg; charset=binary", "image/jpeg", "jpg")] // strips charset suffix
    public void Process_RawFallback_InfersContentTypeFromHint(string sourceHint, string expectedType, string expectedExt)
    {
        var garbage = new byte[] { 0xFF, 0xD8 }; // truncated JPEG header — fails decode

        var result = CoverImageProcessor.Process(garbage, sourceContentType: sourceHint);

        Assert.False(result.WasNormalised);
        Assert.Equal(expectedType, result.ContentType);
        Assert.Equal(expectedExt, result.Extension);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("application/pdf")]
    public void Process_RawFallback_UnknownContentType_BecomesOctetStream(string? hint)
    {
        var garbage = new byte[] { 0x00 };

        var result = CoverImageProcessor.Process(garbage, sourceContentType: hint);

        Assert.False(result.WasNormalised);
        Assert.Equal("application/octet-stream", result.ContentType);
        Assert.Equal("bin", result.Extension);
    }

    private static byte[] MakePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static byte[] MakeJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = 85 });
        return ms.ToArray();
    }
}
