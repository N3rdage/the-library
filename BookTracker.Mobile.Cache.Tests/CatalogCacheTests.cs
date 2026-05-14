using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookTracker.Mobile.Cache;
using BookTracker.Shared.Catalog;
using SkiaSharp;

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
        IReadOnlyList<SeriesSnapshot>? series = null,
        DateTime latestUpdatedAt = default,
        IReadOnlyList<int>? deletedIds = null,
        string version = "test-v1") =>
        new(
            Version: version,
            SyncedAt: new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc),
            Books: books ?? [],
            Authors: authors ?? [],
            Series: series ?? [],
            LatestUpdatedAt: latestUpdatedAt,
            DeletedIds: deletedIds);

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

    [Fact]
    public async Task PopulateAsync_PreservesCoverUrlOnBooks()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                    ["9780553293357"], null, null, "https://covers.example/foundation.jpg"),
            ]));

        var book = await cache.LookupByIsbnAsync("9780553293357");
        Assert.NotNull(book);
        Assert.Equal("https://covers.example/foundation.jpg", book!.CoverUrl);
    }

    // ---- EnsureCoverCachedAsync ----

    [Fact]
    public async Task EnsureCoverCachedAsync_DownloadsResizesAndWritesFile()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                ["9780553293357"], null, null, "https://covers.example/foundation.jpg")]));

        // Source image is 400×600 — wider-than-target-height so resize
        // shrinks the long edge to 200 (so 200/600 * 400 ≈ 133 wide,
        // 200 tall). Resize correctness is verified by re-decoding the
        // written JPEG below.
        var sourceBytes = MakePngBytes(width: 400, height: 600);
        var http = HttpClientReturning(sourceBytes);

        var path = await cache.EnsureCoverCachedAsync(1, http);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        var written = await File.ReadAllBytesAsync(path!);
        using var decoded = SKBitmap.Decode(written);
        Assert.NotNull(decoded);
        // Long edge clamped to 200, aspect ratio preserved.
        Assert.True(Math.Max(decoded!.Width, decoded.Height) <= 200);
        Assert.Equal(200, decoded.Height); // tall source, height stays at long edge

        // Subsequent LookupByIsbn surfaces the path through the snapshot
        // pipeline via CachedBook.CoverPath (not exposed on BookSnapshot
        // directly, but the second EnsureCoverCachedAsync call below
        // proves persistence).
    }

    [Fact]
    public async Task EnsureCoverCachedAsync_ShortCircuitsOnSecondCall()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                ["9780553293357"], null, null, "https://covers.example/foundation.jpg")]));

        var sourceBytes = MakePngBytes(100, 100);
        var counter = new RequestCountingHandler(sourceBytes);
        var http = new HttpClient(counter);

        var path1 = await cache.EnsureCoverCachedAsync(1, http);
        var path2 = await cache.EnsureCoverCachedAsync(1, http);

        Assert.Equal(path1, path2);
        // Second call hits the on-disk file path early and does NOT
        // make a second HTTP request. This is the lazy-on-load
        // payoff — repeated views of the same book cost zero network.
        Assert.Equal(1, counter.RequestCount);
    }

    [Fact]
    public async Task EnsureCoverCachedAsync_ReturnsNullWhenBookNotInCache()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot()); // empty

        var http = HttpClientReturning(MakePngBytes(10, 10));
        Assert.Null(await cache.EnsureCoverCachedAsync(999, http));
    }

    [Fact]
    public async Task EnsureCoverCachedAsync_ReturnsNullWhenCoverUrlIsNull()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                ["9780553293357"], null, null, /* CoverUrl */ null)]));

        // No HTTP request should be made — fail loudly if one is.
        var failing = new HttpClient(new ThrowingHandler());
        Assert.Null(await cache.EnsureCoverCachedAsync(1, failing));
    }

    [Fact]
    public async Task EnsureCoverCachedAsync_ReturnsNullOnDownloadFailure()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                ["9780553293357"], null, null, "https://covers.example/foundation.jpg")]));

        var http = new HttpClient(new ThrowingHandler());
        Assert.Null(await cache.EnsureCoverCachedAsync(1, http));

        // No file written; second attempt with a working handler still
        // tries the network rather than serving a phantom cached path.
        var working = HttpClientReturning(MakePngBytes(50, 50));
        var path = await cache.EnsureCoverCachedAsync(1, working);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task EnsureCoverCachedAsync_ReturnsNullWhenResponseIsNotDecodableImage()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                ["9780553293357"], null, null, "https://covers.example/foundation.jpg")]));

        var notImage = "<!doctype html><html>404</html>"u8.ToArray();
        var http = HttpClientReturning(notImage);
        Assert.Null(await cache.EnsureCoverCachedAsync(1, http));
    }

    // ---- delta sync (ApplyDeltaAsync + LatestUpdatedAt) ----

    [Fact]
    public async Task PopulateAsync_StoresLatestUpdatedAt_RoundTripsViaGetMeta()
    {
        // First-load path: the server sends a full snapshot with the
        // delta watermark already populated. Storing it lets the next
        // refresh send `?since=<token>` rather than re-fetching the
        // full catalog.
        var cache = await NewCacheAsync();
        var token = new DateTime(2026, 5, 14, 8, 30, 0, DateTimeKind.Utc);

        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5, ["9780553293357"], null, null)],
            latestUpdatedAt: token));

        var meta = await cache.GetMetaAsync();
        Assert.NotNull(meta);
        Assert.Equal(token, meta!.LatestUpdatedAt);
    }

    [Fact]
    public async Task ApplyDeltaAsync_UpsertsExistingBook_PreservesCoverPathWhenCoverUrlUnchanged()
    {
        // The cover-cache payoff. After a successful EnsureCoverCachedAsync
        // on a previous load, CoverPath holds the on-disk JPEG path. A
        // delta refresh with the same CoverUrl must not wipe it — that
        // would force a re-download of every thumbnail on every refresh.
        var cache = await NewCacheAsync();
        var initialLatest = new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc);
        var coverUrl = "https://covers.example/foundation.jpg";

        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                ["9780553293357"], null, null, coverUrl)],
            latestUpdatedAt: initialLatest));

        // Simulate a cover-cache hit so CoverPath is populated.
        var http = HttpClientReturning(MakePngBytes(100, 150));
        var coverPath = await cache.EnsureCoverCachedAsync(1, http);
        Assert.NotNull(coverPath);
        Assert.True(File.Exists(coverPath));

        // Now a delta refresh: same Book, same CoverUrl, but the title
        // bumped (e.g. user edited it on the Web side).
        var newLatest = new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc);
        await cache.ApplyDeltaAsync(SampleSnapshot(
            books: [new(1, "Foundation (revised)", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                ["9780553293357"], null, null, coverUrl)],
            latestUpdatedAt: newLatest));

        // CoverPath is preserved → EnsureCoverCachedAsync short-circuits
        // on the next call without re-downloading.
        var counter = new RequestCountingHandler(MakePngBytes(50, 50));
        var http2 = new HttpClient(counter);
        var pathAfter = await cache.EnsureCoverCachedAsync(1, http2);
        Assert.Equal(coverPath, pathAfter);
        Assert.Equal(0, counter.RequestCount);

        // And the new title is visible.
        var book = await cache.LookupByIsbnAsync("9780553293357");
        Assert.Equal("Foundation (revised)", book!.Title);
    }

    [Fact]
    public async Task ApplyDeltaAsync_ClearsCoverPathWhenCoverUrlChanged()
    {
        // CoverUrl changed (user uploaded a new cover) → drop the
        // cached path so EnsureCoverCachedAsync re-fetches from the
        // new URL on next display.
        var cache = await NewCacheAsync();
        var oldUrl = "https://covers.example/foundation-old.jpg";
        var newUrl = "https://covers.example/foundation-new.jpg";

        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                ["9780553293357"], null, null, oldUrl)],
            latestUpdatedAt: new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc)));

        var oldHttp = HttpClientReturning(MakePngBytes(100, 150));
        Assert.NotNull(await cache.EnsureCoverCachedAsync(1, oldHttp));

        // Delta: same Book, new CoverUrl.
        await cache.ApplyDeltaAsync(SampleSnapshot(
            books: [new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5,
                ["9780553293357"], null, null, newUrl)],
            latestUpdatedAt: new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)));

        // Next EnsureCoverCachedAsync must hit the network (CoverPath
        // was cleared) and fetch from the new URL.
        var counter = new RequestCountingHandler(MakePngBytes(50, 50));
        var newHttp = new HttpClient(counter);
        var pathAfter = await cache.EnsureCoverCachedAsync(1, newHttp);
        Assert.NotNull(pathAfter);
        Assert.Equal(1, counter.RequestCount);
    }

    [Fact]
    public async Task ApplyDeltaAsync_InsertsNewBookNotPreviouslyInCache()
    {
        // First sync had Foundation; delta brings in I, Robot for the
        // first time. Both should survive the merge.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5, ["9780553293357"], null, null)],
            latestUpdatedAt: new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc)));

        await cache.ApplyDeltaAsync(SampleSnapshot(
            books: [new(2, "I, Robot", "Isaac Asimov", ["Isaac Asimov"], "Read", 4, ["9780553294385"], null, null)],
            latestUpdatedAt: new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)));

        Assert.NotNull(await cache.LookupByIsbnAsync("9780553293357"));
        Assert.NotNull(await cache.LookupByIsbnAsync("9780553294385"));
        var meta = await cache.GetMetaAsync();
        Assert.Equal(2, meta!.BookCount);
    }

    [Fact]
    public async Task ApplyDeltaAsync_DeletedIdsRemovesBookAndItsIsbnRows()
    {
        // Soft-deleted Book surfaces in DeletedIds. The local row must
        // disappear AND its ISBN join rows go too — otherwise a
        // subsequent ISBN scan would still hit the dead book.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5, ["9780553293357"], null, null),
                new(2, "Doomed", "X", ["X"], "Read", 0, ["9999999999999"], null, null),
            ],
            latestUpdatedAt: new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc)));

        await cache.ApplyDeltaAsync(SampleSnapshot(
            // No Books changes this delta — just a tombstone.
            deletedIds: [2],
            latestUpdatedAt: new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)));

        Assert.NotNull(await cache.LookupByIsbnAsync("9780553293357"));
        // Dead book gone, AND its ISBN no longer resolves.
        Assert.Null(await cache.LookupByIsbnAsync("9999999999999"));
        var meta = await cache.GetMetaAsync();
        Assert.Equal(1, meta!.BookCount);
    }

    [Fact]
    public async Task ApplyDeltaAsync_AuthorsAndSeriesAreFullRewrittenNotMerged()
    {
        // Server always full-lists Authors + Series on every snapshot,
        // so a delta missing an author means the author was deleted.
        // Cache must reflect that — wipe-and-rewrite, not merge.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            authors:
            [
                new(1, "Isaac Asimov", 1, 5),
                new(2, "Discontinued Author", 2, 1),
            ],
            series:
            [
                new(10, "Foundation", "Series", 7),
                new(11, "Discontinued Series", "Series", 3),
            ],
            latestUpdatedAt: new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc)));

        // Delta carries only the surviving rows for both lists.
        await cache.ApplyDeltaAsync(SampleSnapshot(
            authors: [new(1, "Isaac Asimov", 1, 5)],
            series: [new(10, "Foundation", "Series", 7)],
            latestUpdatedAt: new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)));

        var asimov = await cache.SearchAuthorsAsync("Asimov", 10);
        Assert.Single(asimov);
        // Discontinued author gone — search returns no hits.
        Assert.Empty(await cache.SearchAuthorsAsync("Discontinued", 10));

        var meta = await cache.GetMetaAsync();
        Assert.Equal(1, meta!.AuthorCount);
    }

    [Fact]
    public async Task ApplyDeltaAsync_AdvancesLatestUpdatedAtWatermark()
    {
        // The whole point of the round-trip — the new token surfaces
        // on GetMetaAsync so the next refresh can send it as ?since=.
        var cache = await NewCacheAsync();
        var t1 = new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc);

        await cache.PopulateAsync(SampleSnapshot(latestUpdatedAt: t1));
        Assert.Equal(t1, (await cache.GetMetaAsync())!.LatestUpdatedAt);

        await cache.ApplyDeltaAsync(SampleSnapshot(latestUpdatedAt: t2));
        Assert.Equal(t2, (await cache.GetMetaAsync())!.LatestUpdatedAt);
    }

    [Fact]
    public async Task ApplyDeltaAsync_BookCountReflectsPostMergeRowCountNotIncomingDelta()
    {
        // BookCount must be the on-disk row count after upsert +
        // tombstone-delete, not the size of the incoming delta. A
        // delta of "1 changed Book, no tombstones" with 5 pre-existing
        // rows should report 5 — not 1.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "A", "X", ["X"], "Read", 0, [], null, null),
                new(2, "B", "X", ["X"], "Read", 0, [], null, null),
                new(3, "C", "X", ["X"], "Read", 0, [], null, null),
            ],
            latestUpdatedAt: new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc)));

        await cache.ApplyDeltaAsync(SampleSnapshot(
            books: [new(1, "A (revised)", "X", ["X"], "Read", 0, [], null, null)],
            latestUpdatedAt: new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)));

        var meta = await cache.GetMetaAsync();
        Assert.Equal(3, meta!.BookCount);
    }

    // ---- title search ----

    [Fact]
    public async Task SearchBooksByTitleAsync_MatchesCaseInsensitiveSubstring()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "Foundation", "Isaac Asimov", ["Isaac Asimov"], "Read", 5, [], null, null),
                new(2, "Mountains of Madness", "H.P. Lovecraft", ["H.P. Lovecraft"], "Read", 4, [], null, null),
                new(3, "The Hobbit", "J.R.R. Tolkien", ["J.R.R. Tolkien"], "Read", 5, [], null, null),
            ]));

        // Mid-word match across case.
        var mountain = await cache.SearchBooksByTitleAsync("MOUNTAIN", 10);
        Assert.Single(mountain);
        Assert.Equal("Mountains of Madness", mountain[0].Title);

        // Word at the start.
        var found = await cache.SearchBooksByTitleAsync("found", 10);
        Assert.Single(found);
        Assert.Equal("Foundation", found[0].Title);
    }

    [Fact]
    public async Task SearchBooksByTitleAsync_EmptyOrWhitespaceQueryReturnsEmpty()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "X", ["X"], "Read", 0, [], null, null)]));

        Assert.Empty(await cache.SearchBooksByTitleAsync("", 10));
        Assert.Empty(await cache.SearchBooksByTitleAsync("   ", 10));
    }

    [Fact]
    public async Task SearchBooksByTitleAsync_NoMatchReturnsEmpty()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "X", ["X"], "Read", 0, [], null, null)]));

        Assert.Empty(await cache.SearchBooksByTitleAsync("nonexistent", 10));
    }

    [Fact]
    public async Task SearchBooksByTitleAsync_ResultsAlphabeticallySorted()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                // Insertion order deliberately not alphabetical.
                new(3, "Foundation and Empire", "X", ["X"], "Read", 0, [], null, null),
                new(1, "Foundation", "X", ["X"], "Read", 0, [], null, null),
                new(2, "Foundation's Edge", "X", ["X"], "Read", 0, [], null, null),
            ]));

        var hits = await cache.SearchBooksByTitleAsync("foundation", 10);
        Assert.Equal(
            ["Foundation", "Foundation and Empire", "Foundation's Edge"],
            hits.Select(b => b.Title).ToArray());
    }

    [Fact]
    public async Task SearchBooksByTitleAsync_RespectsLimit()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "Foundation 1", "X", ["X"], "Read", 0, [], null, null),
                new(2, "Foundation 2", "X", ["X"], "Read", 0, [], null, null),
                new(3, "Foundation 3", "X", ["X"], "Read", 0, [], null, null),
            ]));

        var hits = await cache.SearchBooksByTitleAsync("foundation", 2);
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public async Task InitAsync_BackfillsTitleLowerForLegacyRowsWrittenBeforeTheColumnExisted()
    {
        // Regression for the title-search-finds-nothing prod report
        // 2026-05-14. sqlite-net-pcl's CreateTableAsync ALTERs to add
        // the new TitleLower column but doesn't populate it — every
        // book already cached on Drew's phone had TitleLower = NULL
        // after the update, so SearchByTitle's LIKE predicate matched
        // zero rows. InitAsync now backfills via UPDATE on every open.
        var path = Path.Combine(Path.GetTempPath(), $"booktracker-cache-backfill-{Guid.NewGuid():N}.db");

        // Step 1 — populate, then nuke TitleLower via a parallel
        // SQLite connection (mirrors the post-ALTER state on Drew's
        // phone where existing rows ended up with NULL).
        {
            var cache = new CatalogCache();
            await cache.InitAsync(path);
            await cache.PopulateAsync(SampleSnapshot(
                books: [new(1, "Foundation", "Asimov", ["Asimov"], "Read", 5, [], null, null)]));

            // Sanity: search hits before sabotage.
            Assert.Single(await cache.SearchBooksByTitleAsync("foundation", 10));

            var parallel = new SQLite.SQLiteAsyncConnection(path);
            await parallel.ExecuteAsync("UPDATE books SET TitleLower = NULL");
            await parallel.CloseAsync();
        }

        // Step 2 — re-open. InitAsync's backfill UPDATE should
        // re-populate TitleLower, and search should find the book.
        {
            var cache = new CatalogCache();
            await cache.InitAsync(path);
            var hits = await cache.SearchBooksByTitleAsync("foundation", 10);
            Assert.Single(hits);
            Assert.Equal("Foundation", hits[0].Title);
        }
    }

    [Fact]
    public async Task ApplyDeltaAsync_UpdatesTitleLowerOnUpsertSoSearchHitsNewTitle()
    {
        // Regression test for the TitleLower invariant. If ApplyDelta
        // upserts a Book but forgets to recompute TitleLower, the
        // search index falls out of sync with the displayed Title —
        // user searches the new title, gets no hit.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Foundation", "X", ["X"], "Read", 0, [], null, null)],
            latestUpdatedAt: new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc)));

        // Search the original title hits.
        Assert.Single(await cache.SearchBooksByTitleAsync("foundation", 10));

        // Delta renames the book.
        await cache.ApplyDeltaAsync(SampleSnapshot(
            books: [new(1, "Renamed Title", "X", ["X"], "Read", 0, [], null, null)],
            latestUpdatedAt: new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)));

        // Old title no longer matches; new title does.
        Assert.Empty(await cache.SearchBooksByTitleAsync("foundation", 10));
        var renamed = await cache.SearchBooksByTitleAsync("renamed", 10);
        Assert.Single(renamed);
        Assert.Equal("Renamed Title", renamed[0].Title);
    }

    // ---- series gaps ----

    [Fact]
    public async Task GetSeriesGapsAsync_ReturnsMissingOrdersForPartiallyOwnedSeries()
    {
        // Foundation series has ExpectedCount=7. User owns #1, #3, #5 →
        // missing #2, #4, #6, #7.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "Foundation", "Asimov", ["Asimov"], "Read", 5, [], SeriesId: 10, SeriesOrder: 1),
                new(2, "Second Foundation", "Asimov", ["Asimov"], "Read", 5, [], SeriesId: 10, SeriesOrder: 3),
                new(3, "Foundation's Edge", "Asimov", ["Asimov"], "Read", 5, [], SeriesId: 10, SeriesOrder: 5),
            ],
            series: [new(10, "Foundation", "Series", 7)]));

        var gaps = await cache.GetSeriesGapsAsync();

        var gap = Assert.Single(gaps);
        Assert.Equal(10, gap.SeriesId);
        Assert.Equal("Foundation", gap.SeriesName);
        Assert.Equal(7, gap.ExpectedCount);
        Assert.Equal(3, gap.OwnedCount);
        Assert.Equal([2, 4, 6, 7], gap.MissingOrders);
    }

    [Fact]
    public async Task GetSeriesGapsAsync_SkipsSeriesWithNullExpectedCount()
    {
        // Open-ended series (no ExpectedCount) — no notion of "complete",
        // so no gap to surface.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [new(1, "Book A", "X", ["X"], "Read", 0, [], SeriesId: 10, SeriesOrder: 1)],
            series: [new(10, "Open-ended Series", "Series", null)]));

        Assert.Empty(await cache.GetSeriesGapsAsync());
    }

    [Fact]
    public async Task GetSeriesGapsAsync_SkipsSeriesUserDoesntOwn()
    {
        // Series exists in the catalog (someone else's recommendation,
        // or a Series imported alongside another Book in the same arc)
        // but user owns zero of them.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books: [],
            series: [new(10, "Foundation", "Series", 7)]));

        Assert.Empty(await cache.GetSeriesGapsAsync());
    }

    [Fact]
    public async Task GetSeriesGapsAsync_SkipsCompletedSeries()
    {
        // User owns all of #1..#3 for a 3-book series → no gaps,
        // not in the result.
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "Vol 1", "X", ["X"], "Read", 0, [], SeriesId: 10, SeriesOrder: 1),
                new(2, "Vol 2", "X", ["X"], "Read", 0, [], SeriesId: 10, SeriesOrder: 2),
                new(3, "Vol 3", "X", ["X"], "Read", 0, [], SeriesId: 10, SeriesOrder: 3),
            ],
            series: [new(10, "Trilogy", "Series", 3)]));

        Assert.Empty(await cache.GetSeriesGapsAsync());
    }

    [Fact]
    public async Task GetSeriesGapsAsync_BooksWithNullSeriesOrderCountTowardOwnedButNotMissing()
    {
        // Book in the series but with no SeriesOrder set (rare —
        // upstream metadata gap). OwnedCount reflects it; MissingOrders
        // doesn't subtract for it (we don't know which slot it fills).
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "Vol 1", "X", ["X"], "Read", 0, [], SeriesId: 10, SeriesOrder: 1),
                new(2, "Vol ?", "X", ["X"], "Read", 0, [], SeriesId: 10, SeriesOrder: null),
            ],
            series: [new(10, "Trilogy", "Series", 3)]));

        var gap = Assert.Single(await cache.GetSeriesGapsAsync());
        Assert.Equal(2, gap.OwnedCount); // both books count
        Assert.Equal([2, 3], gap.MissingOrders); // null-order doesn't fill a slot
    }

    [Fact]
    public async Task GetSeriesGapsAsync_AlphabeticallySortedByName()
    {
        var cache = await NewCacheAsync();
        await cache.PopulateAsync(SampleSnapshot(
            books:
            [
                new(1, "Z1", "X", ["X"], "Read", 0, [], SeriesId: 30, SeriesOrder: 1),
                new(2, "A1", "X", ["X"], "Read", 0, [], SeriesId: 10, SeriesOrder: 1),
                new(3, "M1", "X", ["X"], "Read", 0, [], SeriesId: 20, SeriesOrder: 1),
            ],
            series:
            [
                new(30, "Zeta", "Series", 2),
                new(10, "Alpha", "Series", 2),
                new(20, "Mu", "Series", 2),
            ]));

        var gaps = await cache.GetSeriesGapsAsync();
        Assert.Equal(
            ["Alpha", "Mu", "Zeta"],
            gaps.Select(g => g.SeriesName).ToArray());
    }

    // ---- helpers ----

    private static byte[] MakePngBytes(int width, int height)
    {
        // Solid-colour bitmap encoded as PNG. SkiaSharp's decoder
        // handles PNG + JPEG so test inputs don't need to match the
        // (JPEG) output format.
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static HttpClient HttpClientReturning(byte[] payload) =>
        new(new ConstantBytesHandler(payload));

    private sealed class ConstantBytesHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
    }

    private sealed class RequestCountingHandler(byte[] payload) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("simulated network failure");
    }
}
