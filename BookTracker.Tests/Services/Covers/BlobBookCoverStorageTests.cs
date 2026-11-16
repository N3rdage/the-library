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
    public async Task Mirror_AllowlistedTypeWithUndecodableBytes_IsSkipped(string contentType)
    {
        var result = await MirrorAsync(new ByteArrayContent([0x01, 0x02, 0x03]), contentType);

        Assert.Equal(SourceUrl, result.Url);
        Assert.Equal(CoverMirrorStatus.PermanentRefusal, result.Status);
        Assert.Null(_uploadOptions);
        AssertNothingUploaded();
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("IMAGE/WEBP")]
    public async Task Mirror_DecodableImageWithAllowlistedType_IsNormalisedAndStored(string contentType)
    {
        var result = await MirrorAsync(new ByteArrayContent(MakePng()), contentType);

        Assert.Equal(PublicBase + "/editions/1." + CoverImageProcessor.NormalisedExtension, result.Url.Split('?')[0]);
        Assert.Equal(CoverMirrorStatus.Mirrored, result.Status);
        Assert.Equal(CoverImageProcessor.NormalisedContentType, _uploadOptions?.HttpHeaders.ContentType);
    }

    [Fact]
    public async Task Mirror_BlobUploadThrows_IsTransientFailure()
    {
        _blob.UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Azure.Response<BlobContentInfo>>(new Azure.RequestFailedException("blob service unavailable")));

        var result = await MirrorAsync(new ByteArrayContent(MakePng()), "image/png");

        Assert.Equal(SourceUrl, result.Url);
        Assert.Equal(CoverMirrorStatus.TransientFailure, result.Status);
    }

    [Fact]
    public async Task Mirror_AlreadyManagedUrl_MakesNoRequest()
    {
        const string managedUrl = PublicBase + "/editions/1.jpg?v=1";
        var handler = new FakeHandler(PngResponse());
        var storage = CreateStorage(new HttpClient(handler));

        var result = await storage.MirrorFromUrlAsync(managedUrl, "editions/1", CancellationToken.None);

        Assert.Equal(managedUrl, result.Url);
        Assert.Equal(CoverMirrorStatus.AlreadyManaged, result.Status);
        Assert.Empty(handler.Requests);
        AssertNothingUploaded();
    }

    [Fact]
    public async Task Upload_UndecodableBytes_Throws()
    {
        var storage = CreateStorage(new HttpClient(new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK))));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.UploadAsync([0x01, 0x02, 0x03], "image/png", "editions/1", CancellationToken.None));

        AssertNothingUploaded();
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public async Task Mirror_NonAllowlistedOrMissingType_IsRejected(string? contentType)
    {
        var result = await MirrorAsync(new ByteArrayContent(MakePng()), contentType);

        Assert.Equal(SourceUrl, result.Url);
        Assert.Equal(CoverMirrorStatus.PermanentRefusal, result.Status);
        AssertNothingUploaded();
    }

    [Fact]
    public async Task Mirror_ContentLengthOverCap_IsRejectedWithoutReadingBody()
    {
        var body = new CountingStream(totalBytes: 16);
        var content = new StreamContent(body);
        content.Headers.ContentLength = BlobBookCoverStorage.MaxDownloadBytes + 1;

        var result = await MirrorAsync(content, "image/jpeg");

        Assert.Equal(SourceUrl, result.Url);
        Assert.Equal(CoverMirrorStatus.PermanentRefusal, result.Status);
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

        Assert.Equal(SourceUrl, result.Url);
        Assert.Equal(CoverMirrorStatus.PermanentRefusal, result.Status);
        Assert.True(body.BytesServed < BlobBookCoverStorage.MaxDownloadBytes * 2, "reading should stop shortly after the cap");
        AssertNothingUploaded();
    }

    [Fact]
    public async Task Mirror_UnknownLengthBodyUnderCap_IsAccepted()
    {
        var png = MakePng();
        var body = new CountingStream(totalBytes: png.Length, payload: png);

        var result = await MirrorAsync(new StreamContent(body), "image/gif");

        Assert.StartsWith(PublicBase + "/editions/1." + CoverImageProcessor.NormalisedExtension, result.Url);
        Assert.Equal(CoverMirrorStatus.Mirrored, result.Status);
        Assert.Equal(png.Length, body.BytesServed);
    }

    [Fact]
    public async Task Mirror_BodyThatNeverArrives_IsAbandonedAtTheClientTimeout()
    {
        // Headers arrive at once; the body never does. With ResponseHeadersRead
        // the HttpClient timeout no longer covers the body, so the mirror has
        // to bound the read itself or the sequential mirror loop stalls.
        var content = new StreamContent(new HangingStream());

        var result = await MirrorAsync(content, "image/jpeg", timeout: TimeSpan.FromMilliseconds(250))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(SourceUrl, result.Url);
        Assert.Equal(CoverMirrorStatus.TransientFailure, result.Status);
        AssertNothingUploaded();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, CoverMirrorStatus.PermanentRefusal)]
    [InlineData(HttpStatusCode.Forbidden, CoverMirrorStatus.PermanentRefusal)]
    [InlineData(HttpStatusCode.RequestTimeout, CoverMirrorStatus.TransientFailure)]
    [InlineData(HttpStatusCode.TooManyRequests, CoverMirrorStatus.TransientFailure)]
    [InlineData(HttpStatusCode.InternalServerError, CoverMirrorStatus.TransientFailure)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CoverMirrorStatus.TransientFailure)]
    public async Task Mirror_NonSuccessStatus_IsRejectedAndClassified(HttpStatusCode status, CoverMirrorStatus expected)
    {
        var result = await MirrorAsync(new ByteArrayContent(MakePng()), "image/png", status);

        Assert.Equal(SourceUrl, result.Url);
        Assert.Equal(expected, result.Status);
        AssertNothingUploaded();
    }

    [Theory]
    [InlineData("http://covers.example.org/b/isbn/9780000000000-L.jpg")]
    [InlineData("https://169.254.169.254/metadata")]
    public async Task Mirror_RefusedSourceUrl_MakesNoRequest(string sourceUrl)
    {
        var handler = new FakeHandler(PngResponse());
        var storage = CreateStorage(new HttpClient(handler));

        var result = await storage.MirrorFromUrlAsync(sourceUrl, "editions/1", CancellationToken.None);

        Assert.Equal(sourceUrl, result.Url);
        Assert.Equal(CoverMirrorStatus.PermanentRefusal, result.Status);
        Assert.Empty(handler.Requests);
        AssertNothingUploaded();
    }

    [Fact]
    public async Task Mirror_RedirectToAllowedHttpsUrl_IsFollowedAndStored()
    {
        const string target = "https://cdn.example.org/covers/final.png";
        var handler = new FakeHandler(new Dictionary<string, HttpResponseMessage>
        {
            [SourceUrl] = Redirect(target),
            [target] = PngResponse(),
        });
        var storage = CreateStorage(new HttpClient(handler));

        var result = await storage.MirrorFromUrlAsync(SourceUrl, "editions/1", CancellationToken.None);

        Assert.StartsWith(PublicBase + "/editions/1." + CoverImageProcessor.NormalisedExtension, result.Url);
        Assert.Equal(CoverMirrorStatus.Mirrored, result.Status);
        Assert.Equal<Uri>([new Uri(SourceUrl), new Uri(target)], handler.Requests);
    }

    [Fact]
    public async Task Mirror_RelativeRedirect_IsResolvedAgainstTheCurrentUrl()
    {
        var handler = new FakeHandler(new Dictionary<string, HttpResponseMessage>
        {
            [SourceUrl] = Redirect("/moved/cover.png"),
            ["https://covers.example.org/moved/cover.png"] = PngResponse(),
        });
        var storage = CreateStorage(new HttpClient(handler));

        var result = await storage.MirrorFromUrlAsync(SourceUrl, "editions/1", CancellationToken.None);

        Assert.StartsWith(PublicBase + "/editions/1.", result.Url);
        Assert.Equal(CoverMirrorStatus.Mirrored, result.Status);
    }

    [Theory]
    [InlineData("http://cdn.example.org/covers/final.png")]
    [InlineData("https://10.0.0.5/covers/final.png")]
    [InlineData("https://169.254.169.254/metadata")]
    public async Task Mirror_RedirectToRefusedUrl_IsNotFollowed(string target)
    {
        var handler = new FakeHandler(new Dictionary<string, HttpResponseMessage>
        {
            [SourceUrl] = Redirect(target),
            [new Uri(target).AbsoluteUri] = PngResponse(),
        });
        var storage = CreateStorage(new HttpClient(handler));

        var result = await storage.MirrorFromUrlAsync(SourceUrl, "editions/1", CancellationToken.None);

        Assert.Equal(SourceUrl, result.Url);
        Assert.Equal(CoverMirrorStatus.PermanentRefusal, result.Status);
        Assert.Single(handler.Requests);
        AssertNothingUploaded();
    }

    [Fact]
    public async Task Mirror_FourRedirects_IsAbandoned()
    {
        var handler = new FakeHandler(new Dictionary<string, HttpResponseMessage>
        {
            [SourceUrl] = Redirect("https://hop1.example.org/c.png"),
            ["https://hop1.example.org/c.png"] = Redirect("https://hop2.example.org/c.png"),
            ["https://hop2.example.org/c.png"] = Redirect("https://hop3.example.org/c.png"),
            ["https://hop3.example.org/c.png"] = Redirect("https://hop4.example.org/c.png"),
            ["https://hop4.example.org/c.png"] = PngResponse(),
        });
        var storage = CreateStorage(new HttpClient(handler));

        var result = await storage.MirrorFromUrlAsync(SourceUrl, "editions/1", CancellationToken.None);

        Assert.Equal(SourceUrl, result.Url);
        Assert.Equal(CoverMirrorStatus.PermanentRefusal, result.Status);
        Assert.Equal(CoverFetchPolicy.MaxRedirects + 1, handler.Requests.Count);
        AssertNothingUploaded();
    }

    [Fact]
    public async Task Mirror_RedirectWithoutLocation_IsTreatedAsNonSuccess()
    {
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.Found));
        var storage = CreateStorage(new HttpClient(handler));

        var result = await storage.MirrorFromUrlAsync(SourceUrl, "editions/1", CancellationToken.None);

        Assert.Equal(SourceUrl, result.Url);
        Assert.Equal(CoverMirrorStatus.TransientFailure, result.Status);
        Assert.Single(handler.Requests);
        AssertNothingUploaded();
    }

    private async Task<CoverMirrorResult> MirrorAsync(HttpContent content, string? contentType, HttpStatusCode status = HttpStatusCode.OK, TimeSpan? timeout = null)
    {
        if (contentType is not null) content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        var http = new HttpClient(new FakeHandler(new HttpResponseMessage(status) { Content = content }))
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };
        return await CreateStorage(http).MirrorFromUrlAsync(SourceUrl, "editions/1", CancellationToken.None);
    }

    private BlobBookCoverStorage CreateStorage(HttpClient http)
    {
        var options = Options.Create(new CoverStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            PublicBaseUrl = PublicBase,
        });
        return new BlobBookCoverStorage(options, http, NullLogger<BlobBookCoverStorage>.Instance, _container);
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

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage PngResponse()
    {
        var content = new ByteArrayContent(MakePng());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    // Answers every request through respond and records the requested URLs.
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public FakeHandler(HttpResponseMessage response) : this(_ => response) { }

        // Scripts one response per absolute URL; an unscripted URL gets 404.
        public FakeHandler(Dictionary<string, HttpResponseMessage> byUrl)
            : this(request => byUrl.TryGetValue(request.RequestUri!.AbsoluteUri, out var r) ? r : new HttpResponseMessage(HttpStatusCode.NotFound)) { }

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    // Non-seekable stream whose reads honour the cancellation token and never
    // complete otherwise: a server that sent its headers and then went quiet.
    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("async reads only");
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Non-seekable stream that serves totalBytes (copied from payload when one
    // is given, zero bytes otherwise) and records how much was handed out, so
    // StreamContent reports no Content-Length (chunked / unknown length).
    private sealed class CountingStream(long totalBytes, byte[]? payload = null) : Stream
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
            if (payload is null) Array.Clear(buffer, offset, n);
            else Array.Copy(payload, BytesServed, buffer, offset, n);
            BytesServed += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
