using System.Net;
using System.Net.Http.Headers;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BookTracker.Web.Services.Covers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BookTracker.Tests.Services.Covers;

[Trait("Category", TestCategories.Unit)]
public class BlobBookCoverStorageTests
{
    private const string SourceUrl = "https://covers.example.org/b/isbn/9780000000000-L.jpg";
    private const string PublicBase = "https://blobs.example.org/book-covers";

    private readonly BlobContainerClient _container = Substitute.For<BlobContainerClient>();
    private readonly BlobClient _blob = Substitute.For<BlobClient>();
    private BlobUploadOptions? _uploadOptions;

    public BlobBookCoverStorageTests()
    {
        _container.GetBlobClient(Arg.Any<string>()).Returns(_blob);
        _blob.UploadAsync(Arg.Any<Stream>(), Arg.Do<BlobUploadOptions>(o => _uploadOptions = o), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<Azure.Response<BlobContentInfo>>()));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("IMAGE/WEBP")]
    public async Task Mirror_AllowlistedType_IsStored(string contentType)
    {
        var result = await MirrorAsync(new ByteArrayContent([0x01, 0x02, 0x03]), contentType);

        Assert.StartsWith(PublicBase + "/editions/1.", result);
        Assert.NotNull(_uploadOptions);
        Assert.Equal(contentType.ToLowerInvariant(), _uploadOptions.HttpHeaders.ContentType);
    }

    [Fact]
    public async Task Mirror_DecodableImageUnderCap_IsNormalisedAndStored()
    {
        var result = await MirrorAsync(new ByteArrayContent(MakePng()), "image/png");

        Assert.Equal(PublicBase + "/editions/1." + CoverImageProcessor.NormalisedExtension, result.Split('?')[0]);
        Assert.Equal(CoverImageProcessor.NormalisedContentType, _uploadOptions?.HttpHeaders.ContentType);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public async Task Mirror_NonAllowlistedOrMissingType_IsRejected(string? contentType)
    {
        var result = await MirrorAsync(new ByteArrayContent(MakePng()), contentType);

        Assert.Equal(SourceUrl, result);
        AssertNothingUploaded();
    }

    [Fact]
    public async Task Mirror_ContentLengthOverCap_IsRejectedWithoutReadingBody()
    {
        var body = new CountingStream(totalBytes: 16);
        var content = new StreamContent(body);
        content.Headers.ContentLength = BlobBookCoverStorage.MaxDownloadBytes + 1;

        var result = await MirrorAsync(content, "image/jpeg");

        Assert.Equal(SourceUrl, result);
        Assert.Equal(0, body.BytesServed);
        AssertNothingUploaded();
    }

    [Fact]
    public async Task Mirror_UnknownLengthBodyOverCap_IsRejectedMidStream()
    {
        var body = new CountingStream(totalBytes: BlobBookCoverStorage.MaxDownloadBytes * 4);
        var content = new StreamContent(body);
        // A non-seekable stream gives StreamContent no Content-Length, so the
        // cap can only be enforced on the bytes actually read.
        Assert.Null(content.Headers.ContentLength);

        var result = await MirrorAsync(content, "image/jpeg");

        Assert.Equal(SourceUrl, result);
        Assert.True(body.BytesServed < BlobBookCoverStorage.MaxDownloadBytes * 2, "reading should stop shortly after the cap");
        AssertNothingUploaded();
    }

    [Fact]
    public async Task Mirror_UnknownLengthBodyUnderCap_IsAccepted()
    {
        var body = new CountingStream(totalBytes: 1024);

        var result = await MirrorAsync(new StreamContent(body), "image/gif");

        Assert.StartsWith(PublicBase + "/editions/1.gif", result);
        Assert.Equal(1024, body.BytesServed);
    }

    [Fact]
    public async Task Mirror_NonSuccessStatus_IsRejected()
    {
        var result = await MirrorAsync(new ByteArrayContent(MakePng()), "image/png", HttpStatusCode.NotFound);

        Assert.Equal(SourceUrl, result);
        AssertNothingUploaded();
    }

    private async Task<string> MirrorAsync(HttpContent content, string? contentType, HttpStatusCode status = HttpStatusCode.OK)
    {
        if (contentType is not null) content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        var http = new HttpClient(new FakeHandler(new HttpResponseMessage(status) { Content = content }));
        var options = Options.Create(new CoverStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            PublicBaseUrl = PublicBase,
        });
        var storage = new BlobBookCoverStorage(options, http, NullLogger<BlobBookCoverStorage>.Instance, _container);
        return await storage.MirrorFromUrlAsync(SourceUrl, "editions/1", CancellationToken.None);
    }

    private void AssertNothingUploaded() =>
        _blob.DidNotReceive().UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>());

    private static byte[] MakePng()
    {
        using var bitmap = new SkiaSharp.SKBitmap(20, 30);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class FakeHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    // Non-seekable stream of zero bytes that records how much was handed out,
    // so StreamContent reports no Content-Length (chunked / unknown length).
    private sealed class CountingStream(long totalBytes) : Stream
    {
        public long BytesServed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesServed; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, totalBytes - BytesServed);
            Array.Clear(buffer, offset, n);
            BytesServed += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
