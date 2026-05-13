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
