using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookTracker.Tests.Services;

public class EditionFormatBackfillServiceTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();

    private EditionFormatBackfillService CreateService() =>
        new(_factory, _lookup, NullLogger<EditionFormatBackfillService>.Instance)
        {
            ApiThrottle = TimeSpan.Zero
        };

    private static BookLookupResult ResultWith(string isbn, BookFormat? format) =>
        new(isbn, "T", null, "A", null, [], null, null, "test", format);

    [Fact]
    public async Task RunBackfillAsync_UpdatesEditionsAndStampsMarker()
    {
        await SeedEditionsAsync(
            ("9780000000001", BookFormat.TradePaperback),
            ("9780000000002", BookFormat.TradePaperback));

        _lookup.LookupByIsbnAsync("9780000000001", Arg.Any<CancellationToken>())
            .Returns(ResultWith("9780000000001", BookFormat.MassMarketPaperback));
        _lookup.LookupByIsbnAsync("9780000000002", Arg.Any<CancellationToken>())
            .Returns(ResultWith("9780000000002", BookFormat.Hardcover));

        await CreateService().RunBackfillAsync(CancellationToken.None);

        using var db = _factory.CreateDbContext();
        var byIsbn = db.Editions.ToDictionary(e => e.Isbn!);
        Assert.Equal(BookFormat.MassMarketPaperback, byIsbn["9780000000001"].Format);
        Assert.Equal(BookFormat.Hardcover, byIsbn["9780000000002"].Format);

        var marker = Assert.Single(db.MaintenanceLogs);
        Assert.Equal("BackfillEditionFormats-v1", marker.Name);
        Assert.Contains("Updated 2 of 2", marker.Notes);
    }

    [Fact]
    public async Task RunBackfillAsync_SkipsWhenMarkerPresent()
    {
        await SeedEditionsAsync(("9780000000001", BookFormat.TradePaperback));
        using (var db = _factory.CreateDbContext())
        {
            db.MaintenanceLogs.Add(new MaintenanceLog
            {
                Name = "BackfillEditionFormats-v1",
                CompletedAt = DateTime.UtcNow,
                Notes = "from a previous run"
            });
            await db.SaveChangesAsync();
        }

        await CreateService().RunBackfillAsync(CancellationToken.None);

        await _lookup.DidNotReceiveWithAnyArgs().LookupByIsbnAsync(default!, default);
    }

    [Fact]
    public async Task RunBackfillAsync_LeavesEditionUnchangedWhenLookupReturnsNullFormat()
    {
        await SeedEditionsAsync(("9780000000001", BookFormat.TradePaperback));
        _lookup.LookupByIsbnAsync("9780000000001", Arg.Any<CancellationToken>())
            .Returns(ResultWith("9780000000001", null));

        await CreateService().RunBackfillAsync(CancellationToken.None);

        using var db = _factory.CreateDbContext();
        Assert.Equal(BookFormat.TradePaperback, db.Editions.Single().Format);

        var marker = Assert.Single(db.MaintenanceLogs);
        Assert.Contains("Updated 0 of 1", marker.Notes);
    }

    [Fact]
    public async Task RunBackfillAsync_TreatsLookupExceptionsAsFailuresAndStillStampsMarker()
    {
        await SeedEditionsAsync(
            ("9780000000001", BookFormat.TradePaperback),
            ("9780000000002", BookFormat.TradePaperback));

        _lookup.LookupByIsbnAsync("9780000000001", Arg.Any<CancellationToken>())
            .Returns<Task<BookLookupResult?>>(_ => throw new HttpRequestException("boom"));
        _lookup.LookupByIsbnAsync("9780000000002", Arg.Any<CancellationToken>())
            .Returns(ResultWith("9780000000002", BookFormat.Hardcover));

        await CreateService().RunBackfillAsync(CancellationToken.None);

        using var db = _factory.CreateDbContext();
        var byIsbn = db.Editions.ToDictionary(e => e.Isbn!);
        Assert.Equal(BookFormat.TradePaperback, byIsbn["9780000000001"].Format);
        Assert.Equal(BookFormat.Hardcover, byIsbn["9780000000002"].Format);

        var marker = Assert.Single(db.MaintenanceLogs);
        Assert.Contains("Updated 1 of 2", marker.Notes);
        Assert.Contains("1 lookup failures", marker.Notes);
    }

    private async Task SeedEditionsAsync(params (string Isbn, BookFormat Format)[] editions)
    {
        using var db = _factory.CreateDbContext();
        // Single shared Author across all books — Author.Name has a unique
        // index, so creating `new Author { Name = "Test" }` per iteration
        // would conflict on the second insert under real SQL.
        var sharedAuthor = new Author { Name = "Test" };
        foreach (var (isbn, format) in editions)
        {
            db.Books.Add(new Book
            {
                Title = "Test",
                Works = [new Work { Title = "Test", WorkAuthors = [new WorkAuthor { Author = sharedAuthor, Order = 0 }] }],
                Editions = [new Edition { Isbn = isbn, Format = format, Copies = [new Copy { Condition = BookCondition.Good }] }]
            });
        }
        await db.SaveChangesAsync();
    }
}
