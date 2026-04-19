using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookTracker.Tests.Services;

public class BookGenreBackfillServiceTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();

    private BookGenreBackfillService CreateService() =>
        new(_factory, _lookup, NullLogger<BookGenreBackfillService>.Instance)
        {
            ApiThrottle = TimeSpan.Zero
        };

    private static BookLookupResult ResultWith(string isbn, IReadOnlyList<string> genres) =>
        new(isbn, "T", null, "A", null, genres, null, null, "test");

    [Fact]
    public async Task RunBackfillAsync_ClearsAndReassignsBookGenres()
    {
        await SeedAsync(seedGenres: new[]
        {
            new Genre { Id = 1, Name = "Mystery" },
            new Genre { Id = 2, Name = "Romance" },
            new Genre { Id = 3, Name = "Science Fiction" },
        });

        // Seed a Christie-shaped book that was wrongly tagged Romance + SF
        // by the old buggy matcher.
        await AddBookWithGenresAsync(
            isbn: "9780000000001",
            genreIds: new[] { 1, 2, 3 });

        // Lookup returns realistic Christie subjects — only "Mystery"
        // should survive after the new matcher + denylist.
        _lookup.LookupByIsbnAsync("9780000000001", Arg.Any<CancellationToken>())
            .Returns(ResultWith("9780000000001", new[] { "Detective and mystery stories" }));

        await CreateService().RunBackfillAsync(CancellationToken.None);

        using var db = _factory.CreateDbContext();
        var book = db.Books.Single();
        var ids = db.Entry(book).Collection(b => b.Genres).Query().Select(g => g.Id).ToHashSet();
        Assert.Equal(new[] { 1 }, ids.OrderBy(i => i).ToArray());

        var marker = Assert.Single(db.MaintenanceLogs);
        Assert.Equal("BackfillBookGenres-v1", marker.Name);
        Assert.Contains("Reclassified 1 of 1", marker.Notes);
    }

    [Fact]
    public async Task RunBackfillAsync_SkipsWhenMarkerPresent()
    {
        await SeedAsync(Array.Empty<Genre>());
        await AddBookWithGenresAsync("9780000000001", Array.Empty<int>());
        using (var db = _factory.CreateDbContext())
        {
            db.MaintenanceLogs.Add(new MaintenanceLog
            {
                Name = "BackfillBookGenres-v1",
                CompletedAt = DateTime.UtcNow,
                Notes = "from a previous run"
            });
            await db.SaveChangesAsync();
        }

        await CreateService().RunBackfillAsync(CancellationToken.None);

        await _lookup.DidNotReceiveWithAnyArgs().LookupByIsbnAsync(default!, default);
    }

    [Fact]
    public async Task RunBackfillAsync_HandlesLookupFailures()
    {
        await SeedAsync(new[] { new Genre { Id = 1, Name = "Mystery" } });
        await AddBookWithGenresAsync("9780000000001", new[] { 1 });

        _lookup.LookupByIsbnAsync("9780000000001", Arg.Any<CancellationToken>())
            .Returns<Task<BookLookupResult?>>(_ => throw new HttpRequestException("boom"));

        await CreateService().RunBackfillAsync(CancellationToken.None);

        using var db = _factory.CreateDbContext();
        var marker = Assert.Single(db.MaintenanceLogs);
        Assert.Contains("1 lookup failures", marker.Notes);
        // Existing genre stays — failure path doesn't touch the collection.
        var book = db.Books.Single();
        var ids = db.Entry(book).Collection(b => b.Genres).Query().Select(g => g.Id).ToArray();
        Assert.Equal(new[] { 1 }, ids);
    }

    private async Task SeedAsync(IEnumerable<Genre> seedGenres)
    {
        using var db = _factory.CreateDbContext();
        foreach (var g in seedGenres) db.Genres.Add(g);
        await db.SaveChangesAsync();
    }

    private async Task AddBookWithGenresAsync(string isbn, IEnumerable<int> genreIds)
    {
        using var db = _factory.CreateDbContext();
        var genres = db.Genres.Where(g => genreIds.Contains(g.Id)).ToList();
        db.Books.Add(new Book
        {
            Title = "Test",
            Author = "Test",
            Editions = [new Edition { Isbn = isbn, Format = BookFormat.TradePaperback, Copies = [new Copy { Condition = BookCondition.Good }] }],
            Genres = genres
        });
        await db.SaveChangesAsync();
    }
}
