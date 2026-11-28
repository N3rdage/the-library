using BookTracker.Web.Services.Covers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookTracker.Tests.Services.Covers;

[Trait("Category", TestCategories.Unit)]
public class CoverPhotoUploaderTests
{
    private const string BlobUrl = "https://test/book-covers/editions/7.jpg?v=42";

    private readonly IBookCoverStorage _storage = Substitute.For<IBookCoverStorage>();
    private readonly CoverPhotoUploader _uploader;

    public CoverPhotoUploaderTests()
    {
        _uploader = new CoverPhotoUploader(_storage, NullLogger<CoverPhotoUploader>.Instance);
        _storage.IsEnabled.Returns(true);
    }

    private Task<CoverUploadResult> UploadAsync(byte[]? bytes = null, string contentType = "image/jpeg") =>
        _uploader.UploadAsync(bytes ?? [1, 2, 3], contentType, 7, CancellationToken.None);

    private Task AssertNothingUploadedAsync() =>
        _storage.DidNotReceive().UploadAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task HappyPath_UploadsUnderTheEditionKey_AndReturnsTheUrl()
    {
        var bytes = new byte[] { 1, 2, 3 };
        _storage.UploadAsync(bytes, "image/jpeg", "editions/7", Arg.Any<CancellationToken>()).Returns(BlobUrl);

        var result = await UploadAsync(bytes);

        Assert.True(result.Success);
        Assert.Equal(BlobUrl, result.NewUrl);
    }

    [Fact]
    public async Task TooLarge_IsRefused_WithoutUploading()
    {
        var result = await UploadAsync(new byte[CoverPhotoUploader.MaxUploadBytes + 1]);

        Assert.False(result.Success);
        Assert.Contains("too large", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await AssertNothingUploadedAsync();
    }

    [Fact]
    public async Task ExactlyTheLimit_IsAccepted()
    {
        _storage.UploadAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(BlobUrl);

        var result = await UploadAsync(new byte[CoverPhotoUploader.MaxUploadBytes]);

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("application/pdf")]
    [InlineData("")]
    public async Task WrongContentType_IsRefused_WithoutUploading(string contentType)
    {
        var result = await UploadAsync(contentType: contentType);

        Assert.False(result.Success);
        Assert.Contains("isn't supported", result.ErrorMessage);
        await AssertNothingUploadedAsync();
    }

    [Fact]
    public async Task StorageDisabled_IsRefused_WithoutUploading()
    {
        _storage.IsEnabled.Returns(false);

        var result = await UploadAsync();

        Assert.False(result.Success);
        Assert.Contains("not configured", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(_uploader.IsEnabled);
        await AssertNothingUploadedAsync();
    }

    [Fact]
    public async Task StorageThrows_ReturnsAFailureInsteadOfPropagating()
    {
        _storage.UploadAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cannot decode"));

        var result = await UploadAsync();

        Assert.False(result.Success);
        Assert.Contains("Upload failed", result.ErrorMessage);
        Assert.Null(result.NewUrl);
    }
}
