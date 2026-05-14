using System.Text.Json;
using BookTracker.Mobile.Cache.Models;
using BookTracker.Shared.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using SQLite;

namespace BookTracker.Mobile.Cache;

public class CatalogCache : ICatalogCache
{
    private readonly ILogger<CatalogCache> _logger;

    /// <summary>Default constructor — kept for back-compat with tests
    /// that don't need structured logging. Uses a no-op logger.
    /// Production registration (MauiProgram) calls the DI-friendly
    /// overload that takes an ILogger.</summary>
    public CatalogCache() : this(NullLogger<CatalogCache>.Instance) { }

    public CatalogCache(ILogger<CatalogCache> logger)
    {
        _logger = logger;
    }

    // Meta-store keys. Plain consts rather than an enum so the
    // wire-shape (catalog-cache.js uses the same key names) stays
    // greppable across both implementations.
    private const string MetaKeyVersion = "version";
    private const string MetaKeySyncedAt = "syncedAt";
    private const string MetaKeyBookCount = "bookCount";
    private const string MetaKeyAuthorCount = "authorCount";
    // Server-side max(UpdatedAt | DeletedAt) at the moment of the
    // last apply. Round-tripped to/from the server as the `?since=`
    // delta token. Stored in ISO 8601 ("O") form so the parse path
    // is round-trip-safe across timezones (server is UTC).
    private const string MetaKeyLatestUpdatedAt = "latestUpdatedAt";

    // Cover-resize target — long edge in pixels. Mirrors the Web's
    // CoverStorage normalisation (which caps at 1200px); 200px is the
    // mobile-list display size, so resize at fetch time avoids
    // storing or rendering anything larger than needed.
    private const int CoverLongEdgePx = 200;
    private const int CoverJpegQuality = 80;

    private SQLiteAsyncConnection? _db;
    private string? _coversDir;

    private SQLiteAsyncConnection Db
        => _db ?? throw new InvalidOperationException(
            "CatalogCache.InitAsync must be called before any other method.");

    public async Task InitAsync(string dbPath)
    {
        // Reuse the existing connection if InitAsync is called twice
        // with the same path (idempotent for the common case where
        // the caller doesn't know whether init has happened yet).
        if (_db is not null && _db.DatabasePath == dbPath) return;

        if (_db is not null)
        {
            await _db.CloseAsync();
            _db = null;
        }

        _db = new SQLiteAsyncConnection(dbPath);
        await _db.CreateTableAsync<CachedBook>();
        await _db.CreateTableAsync<CachedBookIsbn>();
        await _db.CreateTableAsync<CachedBookEdition>();
        await _db.CreateTableAsync<CachedBookWork>();
        await _db.CreateTableAsync<CachedAuthor>();
        await _db.CreateTableAsync<CachedSeries>();
        await _db.CreateTableAsync<CachedMeta>();

        // Schema-evolution backfill: sqlite-net-pcl's CreateTableAsync
        // ALTERs an existing table to add new columns, but doesn't
        // populate existing rows. When the TitleLower column landed
        // (alongside the title-search feature), every Book already
        // cached on Drew's phone got the column as NULL — SearchByTitle's
        // LIKE predicate then matched zero rows on the bulk of the
        // catalogue (only newly-upserted Books had the column populated).
        // Backfill on every Init: idempotent (no-op once everything's
        // populated), microseconds for 3000 rows, and survives the next
        // schema bump that needs the same treatment (just add another
        // UPDATE here).
        await _db.ExecuteAsync(
            "UPDATE books SET TitleLower = LOWER(Title) WHERE TitleLower IS NULL OR TitleLower = ''");

        // Covers live alongside the DB file by convention — Mobile's
        // FileSystem.AppDataDirectory holds both. Single dbPath
        // parameter keeps the public Init surface narrow; tests get
        // a temp-dir + nested covers/ for free.
        _coversDir = Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "covers");
        Directory.CreateDirectory(_coversDir);
    }

    public async Task PopulateAsync(CatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Wipe + rewrite in one transaction so a partial populate
        // can't leave us with stale meta over fresh rows (or vice
        // versa). Same atomicity guarantee as the IndexedDB readwrite
        // transaction in catalog-cache.js's populate().
        //
        // Null-coalesces on the collections + strings even though the
        // server-side projection always ToList()'s. Defensive against
        // an older server that didn't ship the new fields (e.g. mobile
        // running ahead of a yet-to-deploy Web change), or a malformed
        // JSON response that left properties null after deserialisation.
        await Db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<CachedBook>();
            conn.DeleteAll<CachedBookIsbn>();
            conn.DeleteAll<CachedBookEdition>();
            conn.DeleteAll<CachedBookWork>();
            conn.DeleteAll<CachedAuthor>();
            conn.DeleteAll<CachedSeries>();
            conn.DeleteAll<CachedMeta>();

            var books = snapshot.Books ?? [];
            foreach (var book in books)
            {
                var title = book.Title ?? "";
                conn.Insert(new CachedBook
                {
                    Id = book.Id,
                    Title = title,
                    TitleLower = title.ToLowerInvariant(),
                    PrimaryAuthor = book.PrimaryAuthor ?? "",
                    AllAuthorsJson = JsonSerializer.Serialize(book.AllAuthors ?? []),
                    Status = book.Status ?? "",
                    Rating = book.Rating,
                    SeriesId = book.SeriesId,
                    SeriesOrder = book.SeriesOrder,
                    CoverUrl = book.CoverUrl,
                    // CoverPath reset to null on every populate — lazy
                    // re-fetch on first display after a refresh. When
                    // delta sync (TODO #33) lands and Populate becomes
                    // an upsert, the path can be preserved across
                    // refreshes when CoverUrl is unchanged.
                    CoverPath = null,
                });
                foreach (var isbn in book.Isbns ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(isbn))
                    {
                        conn.Insert(new CachedBookIsbn
                        {
                            BookId = book.Id,
                            Isbn = isbn,
                        });
                    }
                }
                // Editions + Works — back-compat for older servers
                // that don't ship these yet (deserialised as null).
                // No-op when null/empty.
                foreach (var edition in book.Editions ?? [])
                {
                    conn.Insert(new CachedBookEdition
                    {
                        Id = edition.Id,
                        BookId = book.Id,
                        Isbn = edition.Isbn,
                        Format = edition.Format ?? "",
                        CoverUrl = edition.CoverUrl,
                    });
                }
                foreach (var work in book.Works ?? [])
                {
                    conn.Insert(new CachedBookWork
                    {
                        Id = work.Id,
                        BookId = book.Id,
                        Title = work.Title ?? "",
                        PrimaryAuthor = work.PrimaryAuthor ?? "",
                    });
                }
            }

            var authors = snapshot.Authors ?? [];
            foreach (var author in authors)
            {
                var name = author.Name ?? "";
                conn.Insert(new CachedAuthor
                {
                    Id = author.Id,
                    Name = name,
                    NameLower = name.ToLowerInvariant(),
                    CanonicalId = author.CanonicalId,
                    BookCount = author.BookCount,
                });
            }

            foreach (var s in snapshot.Series ?? [])
            {
                conn.Insert(new CachedSeries
                {
                    Id = s.Id,
                    Name = s.Name ?? "",
                    Type = s.Type ?? "",
                    ExpectedCount = s.ExpectedCount,
                });
            }

            conn.Insert(new CachedMeta { Key = MetaKeyVersion, Value = snapshot.Version ?? "" });
            conn.Insert(new CachedMeta { Key = MetaKeySyncedAt, Value = snapshot.SyncedAt.ToString("O") });
            conn.Insert(new CachedMeta { Key = MetaKeyBookCount, Value = books.Count.ToString() });
            conn.Insert(new CachedMeta { Key = MetaKeyAuthorCount, Value = authors.Count.ToString() });
            // Watermark for delta refreshes. Default(DateTime) means
            // the server never set the field (old build) — store null
            // by skipping the row entirely so GetMetaAsync surfaces
            // LatestUpdatedAt = null and the next refresh defaults to
            // a full load.
            if (snapshot.LatestUpdatedAt != default)
            {
                conn.Insert(new CachedMeta
                {
                    Key = MetaKeyLatestUpdatedAt,
                    Value = snapshot.LatestUpdatedAt.ToString("O"),
                });
            }
        });
    }

    public async Task ApplyDeltaAsync(CatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Delta path. Books upsert (preserving CoverPath when the URL
        // hasn't moved); Authors + Series wipe-and-rewrite (the server
        // always full-lists them on every snapshot, full or delta);
        // DeletedIds remove tombstoned rows. One SQLite transaction
        // wraps the lot so a mid-apply crash can't leave the cache in
        // a half-merged state.
        await Db.RunInTransactionAsync(conn =>
        {
            var incomingBooks = snapshot.Books ?? [];
            foreach (var book in incomingBooks)
            {
                // Existing local row carries the CoverPath we want to
                // preserve when the URL hasn't moved. Find-by-PK is a
                // single index seek.
                var existing = conn.Find<CachedBook>(book.Id);
                var preservedCoverPath = existing is not null
                    && string.Equals(existing.CoverUrl, book.CoverUrl, StringComparison.Ordinal)
                    ? existing.CoverPath
                    : null;

                // InsertOrReplace handles both shapes: new Book (no
                // existing row) and upsert (overwrites every column).
                var title = book.Title ?? "";
                conn.InsertOrReplace(new CachedBook
                {
                    Id = book.Id,
                    Title = title,
                    TitleLower = title.ToLowerInvariant(),
                    PrimaryAuthor = book.PrimaryAuthor ?? "",
                    AllAuthorsJson = JsonSerializer.Serialize(book.AllAuthors ?? []),
                    Status = book.Status ?? "",
                    Rating = book.Rating,
                    SeriesId = book.SeriesId,
                    SeriesOrder = book.SeriesOrder,
                    CoverUrl = book.CoverUrl,
                    // CoverPath survives when CoverUrl unchanged. When
                    // it changed, nulling out lets EnsureCoverCachedAsync
                    // re-fetch the new thumbnail on next display. The
                    // stale file on disk gets overwritten in place
                    // because Path.Combine builds the same {bookId}.jpg
                    // path; no orphan-cleanup pass needed.
                    CoverPath = preservedCoverPath,
                });

                // ISBN join rows: wipe-and-rewrite per Book. Cheap and
                // correctness-trivial (no need to diff individual ISBNs).
                conn.Execute("DELETE FROM book_isbns WHERE BookId = ?", book.Id);
                foreach (var isbn in book.Isbns ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(isbn))
                    {
                        conn.Insert(new CachedBookIsbn
                        {
                            BookId = book.Id,
                            Isbn = isbn,
                        });
                    }
                }

                // Editions + Works: same wipe-and-rewrite pattern as
                // the ISBN join rows. Each row's PK is the server
                // Id, so we explicitly delete by BookId rather than
                // by PK (the incoming Ids would replace, but stale
                // rows from a since-deleted Edition would linger).
                conn.Execute("DELETE FROM book_editions WHERE BookId = ?", book.Id);
                foreach (var edition in book.Editions ?? [])
                {
                    conn.Insert(new CachedBookEdition
                    {
                        Id = edition.Id,
                        BookId = book.Id,
                        Isbn = edition.Isbn,
                        Format = edition.Format ?? "",
                        CoverUrl = edition.CoverUrl,
                    });
                }
                conn.Execute("DELETE FROM book_works WHERE BookId = ?", book.Id);
                foreach (var work in book.Works ?? [])
                {
                    conn.Insert(new CachedBookWork
                    {
                        Id = work.Id,
                        BookId = book.Id,
                        Title = work.Title ?? "",
                        PrimaryAuthor = work.PrimaryAuthor ?? "",
                    });
                }
            }

            // Tombstones — server-side soft-deletes since the last
            // sync token. Delete by PK plus the per-Book join rows
            // (book_isbns / book_editions / book_works) so a dead
            // book doesn't leak through any read path.
            foreach (var deletedId in snapshot.DeletedIds ?? [])
            {
                conn.Delete<CachedBook>(deletedId);
                conn.Execute("DELETE FROM book_isbns WHERE BookId = ?", deletedId);
                conn.Execute("DELETE FROM book_editions WHERE BookId = ?", deletedId);
                conn.Execute("DELETE FROM book_works WHERE BookId = ?", deletedId);
            }

            // Authors + Series — server always full-lists on every
            // snapshot, so we wipe and rewrite. Cheap (hundreds of
            // rows total) and avoids drift from rename / canonical
            // updates that wouldn't propagate via a delta.
            conn.DeleteAll<CachedAuthor>();
            var authors = snapshot.Authors ?? [];
            foreach (var author in authors)
            {
                var name = author.Name ?? "";
                conn.Insert(new CachedAuthor
                {
                    Id = author.Id,
                    Name = name,
                    NameLower = name.ToLowerInvariant(),
                    CanonicalId = author.CanonicalId,
                    BookCount = author.BookCount,
                });
            }

            conn.DeleteAll<CachedSeries>();
            foreach (var s in snapshot.Series ?? [])
            {
                conn.Insert(new CachedSeries
                {
                    Id = s.Id,
                    Name = s.Name ?? "",
                    Type = s.Type ?? "",
                    ExpectedCount = s.ExpectedCount,
                });
            }

            // Recompute book / author counters from the current row
            // count post-merge — incremental tracking would be fragile
            // (Books table mutates via both upsert and tombstone-delete
            // in the same transaction). The Books count after the
            // delta is the sum of all rows; faster + simpler than
            // tracking deltas.
            var bookCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM books");
            var authorCount = authors.Count;

            // Replace meta wholesale rather than UPDATE-per-key — the
            // table is tiny and InsertOrReplace handles the existing /
            // missing-row cases uniformly.
            conn.InsertOrReplace(new CachedMeta { Key = MetaKeyVersion, Value = snapshot.Version ?? "" });
            conn.InsertOrReplace(new CachedMeta { Key = MetaKeySyncedAt, Value = snapshot.SyncedAt.ToString("O") });
            conn.InsertOrReplace(new CachedMeta { Key = MetaKeyBookCount, Value = bookCount.ToString() });
            conn.InsertOrReplace(new CachedMeta { Key = MetaKeyAuthorCount, Value = authorCount.ToString() });
            if (snapshot.LatestUpdatedAt != default)
            {
                conn.InsertOrReplace(new CachedMeta
                {
                    Key = MetaKeyLatestUpdatedAt,
                    Value = snapshot.LatestUpdatedAt.ToString("O"),
                });
            }
        });
    }

    public async Task<BookSnapshot?> LookupByIsbnAsync(string isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn)) return null;
        var row = await Db.Table<CachedBookIsbn>()
            .Where(r => r.Isbn == isbn)
            .FirstOrDefaultAsync();
        if (row is null) return null;

        var book = await Db.FindAsync<CachedBook>(row.BookId);
        return book is null ? null : await ToSnapshotAsync(book);
    }

    public async Task<IReadOnlyList<BookSnapshot>> LookupByAuthorAsync(int canonicalId)
    {
        // Name set: the canonical's own name + every alias's name.
        // Mirrors catalog-cache.js's lookupByAuthor: an author is
        // considered "by" the canonical if either the canonical row
        // OR an alias row has Name appearing in book.allAuthors.
        var matchNames = await Db.Table<CachedAuthor>()
            .Where(a => a.CanonicalId == canonicalId)
            .ToListAsync();
        if (matchNames.Count == 0) return [];

        var nameSet = matchNames.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);

        // Walk all books, filter by intersection with the canonical
        // name set. At the 3000+ books target this is a low-millisecond
        // scan — same call shape as the JS implementation. If profiling
        // ever shows it on the critical path we can denormalise a
        // book_authors join table; for v1 the simple form ships.
        var books = await Db.Table<CachedBook>().ToListAsync();
        var result = new List<BookSnapshot>();
        foreach (var b in books)
        {
            var all = JsonSerializer.Deserialize<List<string>>(b.AllAuthorsJson) ?? [];
            if (all.Any(n => nameSet.Contains(n)))
            {
                result.Add(await ToSnapshotAsync(b, all));
            }
        }
        return result.OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<AuthorSnapshot>> SearchAuthorsAsync(string query, int limit)
    {
        var cap = limit > 0 ? limit : 20;
        var trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0) return [];
        var lower = trimmed.ToLowerInvariant();

        // Use a LIKE substring on the indexed NameLower column. SQLite
        // can't use an index for a leading-wildcard LIKE, so this is
        // a table scan; fine at the author-count scale (hundreds, not
        // thousands).
        var matches = await Db.Table<CachedAuthor>()
            .Where(a => a.NameLower.Contains(lower))
            .ToListAsync();
        if (matches.Count == 0) return [];

        // Resolve each hit to its canonical row; dedupe by canonicalId.
        // Mirrors catalog-cache.js's resolve-to-canonical logic so
        // typing "Bachman" surfaces King's row, not Bachman's.
        var seen = new HashSet<int>();
        var canonicals = new List<CachedAuthor>();
        foreach (var match in matches)
        {
            if (!seen.Add(match.CanonicalId)) continue;
            var canonical = match.Id == match.CanonicalId
                ? match
                : await Db.FindAsync<CachedAuthor>(match.CanonicalId);
            if (canonical is not null) canonicals.Add(canonical);
            if (canonicals.Count >= cap) break;
        }

        return canonicals
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AuthorSnapshot(a.Id, a.Name, a.CanonicalId, a.BookCount))
            .ToList();
    }

    public async Task<IReadOnlyList<BookSnapshot>> SearchBooksByTitleAsync(string query, int limit)
    {
        var cap = limit > 0 ? limit : 20;
        var trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0) return [];
        var lower = trimmed.ToLowerInvariant();

        // Indexed substring scan on TitleLower. SQLite can't use the
        // index for a leading-wildcard LIKE, so this is a table scan
        // at the books-count scale (≤ a few thousand) — sub-millisecond
        // in practice. Sort alphabetically client-side after rehydrating
        // the snapshots; sorting in SQL by TitleLower vs Title doesn't
        // matter for ASCII titles but the C#-side OrdinalIgnoreCase
        // sort matches AuthorBooksPage's convention.
        var matches = await Db.Table<CachedBook>()
            .Where(b => b.TitleLower.Contains(lower))
            .Take(cap)
            .ToListAsync();
        if (matches.Count == 0) return [];

        var snapshots = new List<BookSnapshot>(matches.Count);
        foreach (var book in matches)
        {
            snapshots.Add(await ToSnapshotAsync(book));
        }
        return snapshots
            .OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<SeriesGap>> GetSeriesGapsAsync()
    {
        // Series gaps = for each Series with ExpectedCount set, the
        // 1..ExpectedCount slots the user doesn't own. Skip series with
        // no ExpectedCount (open-ended series — no "complete" target),
        // skip series the user hasn't started (zero owned), and skip
        // series the user has finished (no gaps). The bookshop use
        // case is "I need #2 and #6 of Foundation" — series with
        // nothing to add are noise.
        var seriesWithTarget = (await Db.Table<CachedSeries>().ToListAsync())
            .Where(s => s.ExpectedCount is > 0)
            .ToList();
        if (seriesWithTarget.Count == 0) return [];

        // Pull all books once; we'll bucket them per-series in memory.
        // At the 3000+ books target this is a single table scan that
        // beats N round-trips per series.
        var books = await Db.Table<CachedBook>().ToListAsync();

        var results = new List<SeriesGap>();
        foreach (var series in seriesWithTarget)
        {
            var inSeries = books.Where(b => b.SeriesId == series.Id).ToList();
            if (inSeries.Count == 0) continue; // user hasn't started this series

            // SeriesOrder slots the user occupies. Null orders count
            // toward OwnedCount but don't fill a specific 1..N slot —
            // they neither help nor hurt the missing-orders calc.
            var ownedOrders = inSeries
                .Where(b => b.SeriesOrder is > 0)
                .Select(b => b.SeriesOrder!.Value)
                .ToHashSet();

            var expected = series.ExpectedCount!.Value;
            var missing = Enumerable.Range(1, expected)
                .Where(n => !ownedOrders.Contains(n))
                .ToList();
            if (missing.Count == 0) continue; // user has the full set

            results.Add(new SeriesGap(
                SeriesId: series.Id,
                SeriesName: series.Name,
                SeriesType: series.Type,
                ExpectedCount: expected,
                OwnedCount: inSeries.Count,
                MissingOrders: missing));
        }

        return results
            .OrderBy(g => g.SeriesName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<BookEnrichedDetail?> GetBookEnrichedDetailAsync(int bookId)
    {
        // Confirm the Book exists in the cache before reporting on
        // its Editions/Works — otherwise an orphan Edition row (if
        // one ever crept in) would surface a nonsense detail page.
        var book = await Db.FindAsync<CachedBook>(bookId);
        if (book is null) return null;

        var editionRows = await Db.Table<CachedBookEdition>()
            .Where(e => e.BookId == bookId)
            .ToListAsync();
        var workRows = await Db.Table<CachedBookWork>()
            .Where(w => w.BookId == bookId)
            .ToListAsync();

        var editions = editionRows
            // Format alpha-sort gives a stable order (Hardcover →
            // MassMarketPaperback → Paperback → ...), tie-break by
            // Isbn so two paperback rows don't randomise on each read.
            .OrderBy(e => e.Format, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Isbn ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(e => new EditionSnapshot(e.Id, e.Isbn, e.Format, e.CoverUrl))
            .ToList();
        var works = workRows
            // Server projects in OrderBy(w => w.Id) — same order is
            // useful client-side because that's how the PrimaryAuthor
            // convention picks the "first Work" for series + author
            // rollups elsewhere.
            .OrderBy(w => w.Id)
            .Select(w => new WorkSnapshot(w.Id, w.Title, w.PrimaryAuthor))
            .ToList();

        return new BookEnrichedDetail(editions, works);
    }

    public async Task<CacheMeta?> GetMetaAsync()
    {
        var rows = await Db.Table<CachedMeta>().ToListAsync();
        if (rows.Count == 0) return null;
        var lookup = rows.ToDictionary(r => r.Key, r => r.Value);

        DateTime? syncedAt = null;
        if (lookup.TryGetValue(MetaKeySyncedAt, out var sa)
            && DateTime.TryParse(sa, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            syncedAt = parsed;
        }

        DateTime? latestUpdatedAt = null;
        if (lookup.TryGetValue(MetaKeyLatestUpdatedAt, out var lua)
            && DateTime.TryParse(lua, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var luaParsed))
        {
            latestUpdatedAt = luaParsed;
        }

        return new CacheMeta(
            Version: lookup.GetValueOrDefault(MetaKeyVersion),
            SyncedAt: syncedAt,
            BookCount: int.TryParse(lookup.GetValueOrDefault(MetaKeyBookCount), out var bc) ? bc : 0,
            AuthorCount: int.TryParse(lookup.GetValueOrDefault(MetaKeyAuthorCount), out var ac) ? ac : 0,
            LatestUpdatedAt: latestUpdatedAt);
    }

    private async Task<BookSnapshot> ToSnapshotAsync(CachedBook book)
    {
        var all = JsonSerializer.Deserialize<List<string>>(book.AllAuthorsJson) ?? [];
        return await ToSnapshotAsync(book, all);
    }

    private async Task<BookSnapshot> ToSnapshotAsync(CachedBook book, List<string> allAuthors)
    {
        var isbns = await Db.Table<CachedBookIsbn>()
            .Where(r => r.BookId == book.Id)
            .ToListAsync();
        return new BookSnapshot(
            book.Id,
            book.Title,
            book.PrimaryAuthor,
            allAuthors,
            book.Status,
            book.Rating,
            isbns.Select(r => r.Isbn).ToList(),
            book.SeriesId,
            book.SeriesOrder,
            book.CoverUrl);
    }

    public async Task<string?> EnsureCoverCachedAsync(int bookId, HttpClient http, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);

        var book = await Db.FindAsync<CachedBook>(bookId);
        if (book is null)
        {
            _logger.LogInformation("Cover cache miss: book {BookId} not in cache", bookId);
            return null;
        }

        // Already cached and the file still exists on disk: short-
        // circuit. Covers can be evicted by the OS under storage
        // pressure (Android external cache rules) — re-download if
        // the path is stale.
        if (book.CoverPath is not null && File.Exists(book.CoverPath))
        {
            _logger.LogDebug("Cover already cached for book {BookId} at {Path}", bookId, book.CoverPath);
            return book.CoverPath;
        }

        if (string.IsNullOrWhiteSpace(book.CoverUrl))
        {
            _logger.LogInformation(
                "Cover cache skip: book {BookId} has no CoverUrl in the cache row " +
                "(common cause: catalog snapshot served by the server predates the " +
                "BookSnapshot.CoverUrl field — refresh the catalog after Bookcase is " +
                "redeployed with the projection)", bookId);
            return null;
        }
        if (_coversDir is null)
        {
            _logger.LogWarning("Cover cache skip: covers dir is null — InitAsync was not called");
            return null;
        }

        _logger.LogInformation("Fetching cover for book {BookId} from {CoverUrl}", bookId, book.CoverUrl);
        byte[] sourceBytes;
        try
        {
            sourceBytes = await http.GetByteArrayAsync(book.CoverUrl, ct);
        }
        catch (Exception ex)
        {
            // Offline, 404, DNS failure, cancellation — leave CoverPath
            // null so the next display attempt retries. Logged at
            // Warning so logcat surfaces it without paranoid Error-level
            // alerting (cover misses self-heal on the next attempt).
            _logger.LogWarning(ex,
                "Cover fetch failed for book {BookId} from {CoverUrl}", bookId, book.CoverUrl);
            return null;
        }

        var resizedJpeg = ResizeToJpeg(sourceBytes);
        if (resizedJpeg is null)
        {
            // Bytes weren't a decodable image. Same fall-through —
            // null CoverPath so we'd retry later (in practice this
            // means a broken upstream cover, but retry costs nothing
            // and self-heals if upstream fixes it).
            _logger.LogWarning(
                "Cover decode/resize returned null for book {BookId} ({ByteCount} bytes downloaded from {CoverUrl})",
                bookId, sourceBytes.Length, book.CoverUrl);
            return null;
        }

        var destPath = Path.Combine(_coversDir, $"{bookId}.jpg");
        try
        {
            await File.WriteAllBytesAsync(destPath, resizedJpeg, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cover write failed for book {BookId} to {DestPath}", bookId, destPath);
            return null;
        }

        // Persist the path so next session skips the download. UPDATE
        // by primary key — single-row touch.
        book.CoverPath = destPath;
        await Db.UpdateAsync(book);

        _logger.LogInformation(
            "Cover cached for book {BookId} at {DestPath} ({JpegByteCount} bytes JPEG)",
            bookId, destPath, resizedJpeg.Length);
        return destPath;
    }

    /// <summary>Decodes the supplied image bytes, resizes to a
    /// <see cref="CoverLongEdgePx"/>-long-edge variant maintaining
    /// aspect ratio, and re-encodes as JPEG. Returns null when the
    /// input isn't a decodable image. Synchronous SkiaSharp work
    /// wrapped in a method so the cover-cache path is the only caller.
    ///
    /// SkiaSharp.Decode can throw or return null on malformed bytes
    /// — wrap both in try/catch so a broken upstream response surfaces
    /// as "couldn't cache" rather than crashing the caller.
    ///
    /// SkiaSharp 3.x replaced SKFilterQuality with SKSamplingOptions
    /// on the Resize signature. Mitchell cubic resampler matches the
    /// previous SKFilterQuality.High treatment for downscaling — a
    /// good fit for book cover thumbnails.
    /// </summary>
    private static byte[]? ResizeToJpeg(byte[] sourceBytes)
    {
        SKBitmap? source;
        try
        {
            source = SKBitmap.Decode(sourceBytes);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
        if (source is null) return null;
        using (source)
        {
            var (width, height) = ScaleToLongEdge(source.Width, source.Height, CoverLongEdgePx);
            using var resized = source.Resize(
                new SKImageInfo(width, height, source.ColorType, source.AlphaType),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
            if (resized is null) return null;

            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, CoverJpegQuality);
            return encoded.ToArray();
        }
    }

    private static (int Width, int Height) ScaleToLongEdge(int srcWidth, int srcHeight, int longEdge)
    {
        if (srcWidth <= 0 || srcHeight <= 0) return (longEdge, longEdge);
        if (srcWidth >= srcHeight)
        {
            if (srcWidth <= longEdge) return (srcWidth, srcHeight);
            var scaled = (int)Math.Round(srcHeight * (double)longEdge / srcWidth);
            return (longEdge, Math.Max(1, scaled));
        }
        else
        {
            if (srcHeight <= longEdge) return (srcWidth, srcHeight);
            var scaled = (int)Math.Round(srcWidth * (double)longEdge / srcHeight);
            return (Math.Max(1, scaled), longEdge);
        }
    }
}
