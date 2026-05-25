using BookTracker.Mobile.Cache;
using BookTracker.Shared.Wishlist;

namespace BookTracker.Mobile.Cache.Tests;

// PR D wishlist cache tests — pure SQLite, no MAUI runtime, mirroring
// the CatalogCacheTests shape.
[Trait("Category", "Unit")]
public class WishlistCacheTests
{
    private static async Task<CatalogCache> NewCacheAsync()
    {
        var cache = new CatalogCache();
        var path = Path.Combine(Path.GetTempPath(), $"booktracker-wishlist-test-{Guid.NewGuid():N}.db");
        await cache.InitAsync(path);
        return cache;
    }

    private static WishlistSnapshot Snapshot(params WishlistItemSnapshot[] items) =>
        new(
            Version: "test-v1",
            SyncedAt: new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc),
            Items: items);

    private static WishlistItemSnapshot Item(
        int id,
        string title = "Title",
        string author = "Author",
        string priority = "Medium",
        string? cover = null,
        IReadOnlyList<string>? isbns = null,
        int? seriesId = null,
        int? seriesOrder = null) =>
        new(
            id, title, author, priority,
            Isbn: null,
            SeriesId: seriesId,
            SeriesOrder: seriesOrder,
            DateAdded: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            CoverUrl: cover,
            Isbns: isbns);

    [Fact]
    public async Task PopulateWishlistAsync_RoundTripsItemFieldsAndIsbns()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateWishlistAsync(Snapshot(
            Item(1, "Foundation", "Asimov", "High",
                cover: "https://covers.example/foundation.jpg",
                isbns: ["9780553293357", "9780553382570"]),
            Item(2, "The Hobbit", "Tolkien", "Medium",
                isbns: ["9780261103252"])));

        var loaded = await cache.GetWishlistAsync();
        Assert.Equal(2, loaded.Count);
        var foundation = loaded.Single(i => i.Id == 1);
        Assert.Equal("Foundation", foundation.Title);
        Assert.Equal("Asimov", foundation.Author);
        Assert.Equal("High", foundation.Priority);
        Assert.Equal("https://covers.example/foundation.jpg", foundation.CoverUrl);
        Assert.NotNull(foundation.Isbns);
        Assert.Equal(2, foundation.Isbns!.Count);
        Assert.Contains("9780553293357", foundation.Isbns);
        Assert.Contains("9780553382570", foundation.Isbns);
    }

    [Fact]
    public async Task GetWishlistAsync_OrdersByPriorityHighThenMediumThenLow()
    {
        // High before Medium before Low — the in-shop "what next to
        // buy" ranking. Within the same priority the sort is by
        // DateAdded ascending, but this test only covers the priority
        // ordering itself.
        var cache = await NewCacheAsync();
        await cache.PopulateWishlistAsync(Snapshot(
            Item(1, "Low pri", priority: "Low"),
            Item(2, "High pri", priority: "High"),
            Item(3, "Medium pri", priority: "Medium")));

        var loaded = await cache.GetWishlistAsync();
        Assert.Equal(["High pri", "Medium pri", "Low pri"], loaded.Select(i => i.Title));
    }

    [Fact]
    public async Task IsWishlistedIsbnAsync_MatchesAnyKnownIsbnOnTheItem()
    {
        // The snapshot ships every known ISBN (server-side union of
        // legacy + new ISBN table). The scan-flag matches against any
        // of them.
        var cache = await NewCacheAsync();
        await cache.PopulateWishlistAsync(Snapshot(
            Item(1, "Foundation",
                isbns: ["9780553293357", "9780553382570"])));

        Assert.True(await cache.IsWishlistedIsbnAsync("9780553293357"));
        Assert.True(await cache.IsWishlistedIsbnAsync("9780553382570"));
        Assert.False(await cache.IsWishlistedIsbnAsync("9789999999999"));
    }

    [Fact]
    public async Task IsWishlistedIsbnAsync_FalseForEmptyOrWhitespace()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateWishlistAsync(Snapshot(Item(1, isbns: ["9780000000001"])));
        Assert.False(await cache.IsWishlistedIsbnAsync(""));
        Assert.False(await cache.IsWishlistedIsbnAsync("   "));
    }

    [Fact]
    public async Task MarkBoughtLocally_HidesItemFromGetWishlist_ButLeavesCacheRowIntact()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateWishlistAsync(Snapshot(
            Item(1, "Foundation"),
            Item(2, "The Hobbit")));

        await cache.MarkBoughtLocallyAsync(1);
        var loaded = await cache.GetWishlistAsync();

        var sole = Assert.Single(loaded);
        Assert.Equal(2, sole.Id);
    }

    [Fact]
    public async Task MarkBoughtLocally_AlsoSuppressesScanFlag()
    {
        // A book the user just marked bought shouldn't keep flagging
        // "on your wishlist" on subsequent scans. The scan-flag honors
        // the bought-local set.
        var cache = await NewCacheAsync();
        await cache.PopulateWishlistAsync(Snapshot(
            Item(1, "Foundation", isbns: ["9780553293357"])));

        Assert.True(await cache.IsWishlistedIsbnAsync("9780553293357"));
        await cache.MarkBoughtLocallyAsync(1);
        Assert.False(await cache.IsWishlistedIsbnAsync("9780553293357"));
    }

    [Fact]
    public async Task UnmarkBoughtLocally_RestoresVisibility()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateWishlistAsync(Snapshot(Item(1, "Foundation")));

        await cache.MarkBoughtLocallyAsync(1);
        Assert.Empty(await cache.GetWishlistAsync());

        await cache.UnmarkBoughtLocallyAsync(1);
        Assert.Single(await cache.GetWishlistAsync());
    }

    [Fact]
    public async Task PopulateWishlistAsync_PreservesBoughtLocalEntriesAcrossRefresh()
    {
        // Bought-local entries survive catalog refresh — orphan-tolerant.
        // If the server row is gone (Drew captured the book via
        // Bookcase) the bought-local entry becomes a harmless no-op
        // (the GetWishlist join yields nothing for the missing id).
        var cache = await NewCacheAsync();
        await cache.PopulateWishlistAsync(Snapshot(
            Item(1, "Foundation"),
            Item(2, "The Hobbit")));

        await cache.MarkBoughtLocallyAsync(1);

        // Server-side refresh: item 1 still on the list (user hasn't
        // captured it via Bookcase yet). The bought-local should keep
        // hiding it.
        await cache.PopulateWishlistAsync(Snapshot(
            Item(1, "Foundation"),
            Item(2, "The Hobbit"),
            Item(3, "Dune")));

        var loaded = await cache.GetWishlistAsync();
        Assert.Equal(2, loaded.Count);
        Assert.DoesNotContain(loaded, i => i.Id == 1);
    }

    [Fact]
    public async Task PopulateWishlistAsync_OrphanBoughtLocalRowsBecomeHarmless()
    {
        // Item 1 is marked bought locally. Then a server refresh comes
        // back without item 1 (Drew captured it via Bookcase, server-
        // side row is gone). The bought-local entry is now an orphan
        // but causes no UI weirdness — it just doesn't match any cached
        // row in the GetWishlist join.
        var cache = await NewCacheAsync();
        await cache.PopulateWishlistAsync(Snapshot(
            Item(1, "Foundation"),
            Item(2, "The Hobbit")));

        await cache.MarkBoughtLocallyAsync(1);

        // Server refresh — item 1 is gone.
        await cache.PopulateWishlistAsync(Snapshot(Item(2, "The Hobbit")));

        var loaded = await cache.GetWishlistAsync();
        var sole = Assert.Single(loaded);
        Assert.Equal(2, sole.Id);
    }

    [Fact]
    public async Task PopulateWishlistAsync_ReplacesPreviousItems()
    {
        // The wishlist is small enough to wipe-and-rewrite (no delta
        // semantics). A second populate with a shrunken list shrinks
        // the cache.
        var cache = await NewCacheAsync();
        await cache.PopulateWishlistAsync(Snapshot(
            Item(1, "A"), Item(2, "B"), Item(3, "C")));
        Assert.Equal(3, (await cache.GetWishlistAsync()).Count);

        await cache.PopulateWishlistAsync(Snapshot(Item(2, "B")));
        var loaded = await cache.GetWishlistAsync();
        Assert.Single(loaded);
        Assert.Equal(2, loaded[0].Id);
    }
}
