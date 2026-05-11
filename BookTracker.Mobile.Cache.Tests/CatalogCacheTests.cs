using BookTracker.Mobile.Cache;
using BookTracker.Shared.Catalog;

namespace BookTracker.Mobile.Cache.Tests;

// "Unit" so CI's filter (Category=Unit|Component|Integration) picks
// these up. They're unit-scoped — pure C# + SQLite, no MAUI runtime,
// no network, no Testcontainer.
[Trait("Category", "Unit")]
public class CatalogCacheTests
{
    // Each test gets its own SQLite file in the OS temp directory.
    // sqlite-net-pcl treats the path as a literal filename (no
    // query-string support) and pools connections per path, so
    // ":memory:" can't be used directly without bleeding state
    // across tests. GUID-named files are small (<100KB) and the OS
    // cleans temp eventually; no explicit teardown needed.
    private static async Task<CatalogCache> NewCacheAsync()
    {
        var cache = new CatalogCache();
        var path = Path.Combine(Path.GetTempPath(), $"booktracker-cache-test-{Guid.NewGuid():N}.db");
        await cache.InitAsync(path);
        return cache;
    }

    private static CatalogSnapshot SampleSnapshot(
        IReadOnlyList<BookSnapshot>? books = null,
        IReadOnlyList<AuthorSnapshot>? authors = null,
        IReadOnlyList<SeriesSnapshot>? series = null) =>
        new(
            Version: "test-v1",
            SyncedAt: new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc),
            Books: books ?? [],
            Authors: authors ?? [],
            Series: series ?? []);

    [Fact]
    public async Task GetMetaAsync_ReturnsNullBeforePopulate()
    {
        var cache = await NewCacheAsync();
        Assert.Null(await cache.GetMetaAsync());
    }

    [Fact]
    public async Task PopulateAsync_WritesMetaCountersAndTimestamp()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5, ["9780553293357"], null, null)],
            authors: [new(1, "Isaac Asimov", 1, 1)]));

        var meta = await cache.GetMetaAsync();
        Assert.NotNull(meta);
        Assert.Equal("test-v1", meta!.Version);
        Assert.Equal(new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc), meta.SyncedAt);
        Assert.Equal(1, meta.BookCount);
        Assert.Equal(1, meta.AuthorCount);
    }

    [Fact]
    public async Task LookupByIsbnAsync_FindsByExactMatch()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                    ["9780553293357", "9780553382570"], null, null),
                new(2, "Dune", "Frank Herbert", ["Frank Herbert"], "Read", 5, ["9780441172719"], null, null),
            ]));

        var hit = await cache.LookupByIsbnAsync("9780553293357");
        Assert.NotNull(hit);
        Assert.Equal(1, hit!.Id);
        Assert.Equal("Foundation", hit.Title);
        // The book's secondary ISBN should also resolve to the same book.
        var alsoHit = await cache.LookupByIsbnAsync("9780553382570");
        Assert.NotNull(alsoHit);
        Assert.Equal(1, alsoHit!.Id);
    }

    [Fact]
    public async Task LookupByIsbnAsync_ReturnsNullForUnknownIsbn()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot());
        Assert.Null(await cache.LookupByIsbnAsync("9999999999999"));
        Assert.Null(await cache.LookupByIsbnAsync(""));
    }

    [Fact]
    public async Task SearchAuthorsAsync_MatchesCaseInsensitiveSubstring()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(authors:
        [
            new(1, "Isaac Asimov", 1, 12),
            new(2, "Arthur C. Clarke", 2, 5),
            new(3, "Stephen King", 3, 20),
        ]));

        var asi = await cache.SearchAuthorsAsync("asi", 10);
        Assert.Single(asi);
        Assert.Equal("Isaac Asimov", asi[0].Name);

        // Lowercased query also hits.
        var king = await cache.SearchAuthorsAsync("KING", 10);
        Assert.Single(king);
        Assert.Equal("Stephen King", king[0].Name);
    }

    [Fact]
    public async Task SearchAuthorsAsync_ResolvesAliasToCanonical()
    {
        // King (canonical, Id=1) with Bachman alias (Id=2, CanonicalId=1).
        // Typing "Bachman" should surface the King row, deduped to the
        // canonical — mirrors the JS implementation's behaviour.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(authors:
        [
            new(1, "Stephen King", 1, 20),
            new(2, "Richard Bachman", 1, 5),
        ]));

        var hit = await cache.SearchAuthorsAsync("Bachman", 10);
        Assert.Single(hit);
        // Single result, and it's the canonical (King), not the alias.
        Assert.Equal("Stephen King", hit[0].Name);
        Assert.Equal(1, hit[0].Id);
    }

    [Fact]
    public async Task SearchAuthorsAsync_EmptyQueryReturnsEmpty()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(authors: [new(1, "X", 1, 1)]));
        Assert.Empty(await cache.SearchAuthorsAsync("", 10));
        Assert.Empty(await cache.SearchAuthorsAsync("   ", 10));
    }

    [Fact]
    public async Task LookupByAuthorAsync_IncludesBooksUnderAliasNames()
    {
        // Carrie credited to King; The Long Walk credited to Bachman.
        // LookupByAuthor(canonical=King.Id) should return both.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "Carrie", "Stephen King", ["Stephen King"], "Read", 4, ["9780307743657"], null, null),
                new(2, "The Long Walk", "Richard Bachman", ["Richard Bachman"], "Read", 5, ["9781501144202"], null, null),
                // Unrelated book — should NOT appear.
                new(3, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5, ["9780553293357"], null, null),
            ],
            authors:
            [
                new(1, "Stephen King", 1, 2),
                new(2, "Richard Bachman", 1, 1),
                new(3, "Isaac Asimov", 3, 1),
            ]));

        var byKing = await cache.LookupByAuthorAsync(1);
        Assert.Equal(2, byKing.Count);
        Assert.Contains(byKing, b => b.Title == "Carrie");
        Assert.Contains(byKing, b => b.Title == "The Long Walk");
        // Sorted by title alphabetically.
        Assert.Equal(["Carrie", "The Long Walk"], byKing.Select(b => b.Title));
    }

    [Fact]
    public async Task LookupByAuthorAsync_EmptyForUnknownCanonical()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot());
        Assert.Empty(await cache.LookupByAuthorAsync(9999));
    }

    [Fact]
    public async Task PopulateAsync_ReplacesPriorContent()
    {
        // A second Populate with a smaller catalog must shrink the
        // cache, not merge. Same atomicity expectation as the JS
        // implementation's clear-then-insert transaction.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Old book", "X", ["X"], "Read", 0, ["1234567890123"], null, null)]));

        await cache.PopulateAsync(SampleSnapshot(
            books: [new(2, "New book", "Y", ["Y"], "Read", 0, ["9876543210123"], null, null)]));

        Assert.Null(await cache.LookupByIsbnAsync("1234567890123"));
        var newHit = await cache.LookupByIsbnAsync("9876543210123");
        Assert.NotNull(newHit);
        Assert.Equal("New book", newHit!.Title);
    }

    [Fact]
    public async Task PopulateAsync_PreservesSeriesFieldsOnBooks()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                    ["9780553293357"], SeriesId: 42, SeriesOrder: 1),
            ],
            series: [new(42, "Foundation", "Series", 7)]));

        var book = await cache.LookupByIsbnAsync("9780553293357");
        Assert.NotNull(book);
        Assert.Equal(42, book!.SeriesId);
        Assert.Equal(1, book.SeriesOrder);
    }
}
