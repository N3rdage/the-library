using BookTracker.Data.Models;
using BookTracker.Web.Services.Covers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookTracker.Tests.Services.Covers;

// One mirror tick against real SQL with a faked IBookCoverStorage: the mirror
// writes the blob URL only when the stored URL still equals the one it
// downloaded, so a cover the user changes mid-download is not overwritten.
[Trait("Category", TestCategories.Integration)]
public class CoverMirrorBackgroundServiceTests
{
    private const string UpstreamUrl = "https://covers.example.org/b/isbn/9780553293357-L.jpg";
    private const string UserUrl = "https://covers.example.org/b/isbn/9780553293357-M.jpg";
    private const string BlobUrl = "https://storage.example.net/book-covers/covers/mirrored.jpg";

    private readonly TestDbContextFactory _factory = new();
    private readonly IBookCoverStorage _storage = Substitute.For<IBookCoverStorage>();

    public CoverMirrorBackgroundServiceTests()
    {
        _storage.IsEnabled.Returns(true);
        _storage.IsManagedUrl(Arg.Any<string?>()).Returns(false);
    }

    [Fact]
    public async Task EditionUrlChangedDuringDownload_KeepsTheUsersUrl_AndStaysDue()
    {
        var (_, editionId) = await SeedAsync(editionCoverUrl: UpstreamUrl, bookCoverUrl: null);
        await AddElapsedTransientStateAsync(CoverMirrorTarget.Edition, editionId, UpstreamUrl);
        MirrorReturnsBlobAfter(async () =>
        {
            await using var db = _factory.CreateDbContext();
            var edition = await db.Editions.SingleAsync(e => e.Id == editionId);
            edition.CoverUrl = UserUrl;
            await db.SaveChangesAsync();
        });

        await RunTickAsync();

        await using var check = _factory.CreateDbContext();
        var stored = await check.Editions.SingleAsync(e => e.Id == editionId);
        Assert.Equal(UserUrl, stored.CoverUrl);
        var due = await CoverMirrorQueue.DueEditionsAsync(check, DateTime.UtcNow, CancellationToken.None);
        Assert.Contains(due, c => c.Id == editionId && c.Url == UserUrl);
    }

    [Fact]
    public async Task BookUrlChangedDuringDownload_KeepsTheUsersUrl()
    {
        var (bookId, _) = await SeedAsync(editionCoverUrl: null, bookCoverUrl: UpstreamUrl);
        MirrorReturnsBlobAfter(async () =>
        {
            await using var db = _factory.CreateDbContext();
            var book = await db.Books.SingleAsync(b => b.Id == bookId);
            book.SetCover(UserUrl);
            await db.SaveChangesAsync();
        });

        await RunTickAsync();

        await using var check = _factory.CreateDbContext();
        var stored = await check.Books.SingleAsync(b => b.Id == bookId);
        Assert.Equal(UserUrl, stored.DefaultCoverArtUrl);
        var due = await CoverMirrorQueue.DueBooksAsync(check, DateTime.UtcNow, CancellationToken.None);
        Assert.Contains(due, c => c.Id == bookId && c.Url == UserUrl);
    }

    [Fact]
    public async Task EditionUrlUnchanged_StoresTheBlobUrl_AndClearsTheState()
    {
        var (_, editionId) = await SeedAsync(editionCoverUrl: UpstreamUrl, bookCoverUrl: null);
        await AddElapsedTransientStateAsync(CoverMirrorTarget.Edition, editionId, UpstreamUrl);
        MirrorReturnsBlobAfter(() => Task.CompletedTask);

        await RunTickAsync();

        await using var check = _factory.CreateDbContext();
        var stored = await check.Editions.SingleAsync(e => e.Id == editionId);
        Assert.Equal(BlobUrl, stored.CoverUrl);
        Assert.False(await check.CoverMirrorStates.AnyAsync(
            s => s.Target == CoverMirrorTarget.Edition && s.TargetId == editionId));
    }

    private void MirrorReturnsBlobAfter(Func<Task> duringDownload) =>
        _storage.MirrorFromUrlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await duringDownload();
                return new CoverMirrorResult(BlobUrl, CoverMirrorStatus.Mirrored);
            });

    private Task RunTickAsync() =>
        CoverMirrorBackgroundService.ProcessTickAsync(
            _storage, _factory, NullLogger.Instance, CancellationToken.None);

    private async Task<(int BookId, int EditionId)> SeedAsync(string? editionCoverUrl, string? bookCoverUrl)
    {
        await using var db = _factory.CreateDbContext();
        var edition = new Edition { Isbn = "9780553293357", CoverUrl = editionCoverUrl };
        var book = TestBooks.Book(
            "Foundation",
            coverUrl: bookCoverUrl,
            editions: [edition]);
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return (book.Id, edition.Id);
    }

    private async Task AddElapsedTransientStateAsync(CoverMirrorTarget target, int targetId, string url)
    {
        var now = DateTime.UtcNow;
        await using var db = _factory.CreateDbContext();
        db.CoverMirrorStates.Add(new CoverMirrorState
        {
            Target = target,
            TargetId = targetId,
            Url = url,
            Attempts = 1,
            LastAttemptUtc = now.AddMinutes(-2),
            NextAttemptUtc = now.AddMinutes(-1),
            LastOutcome = CoverMirrorOutcome.TransientFailure,
        });
        await db.SaveChangesAsync();
    }
}
