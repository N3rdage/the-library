using System.Text.Json;
using BookTracker.Mobile.Cache.Models;
using BookTracker.Shared.Catalog;
using SQLite;

namespace BookTracker.Mobile.Cache;

public class CatalogCache : ICatalogCache
{
    // Meta-store keys. Plain consts rather than an enum so the
    // wire-shape (catalog-cache.js uses the same key names) stays
    // greppable across both implementations.
    private const string MetaKeyVersion = "version";
    private const string MetaKeySyncedAt = "syncedAt";
    private const string MetaKeyBookCount = "bookCount";
    private const string MetaKeyAuthorCount = "authorCount";

    private SQLiteAsyncConnection? _db;

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
        await _db.CreateTableAsync<CachedAuthor>();
        await _db.CreateTableAsync<CachedSeries>();
        await _db.CreateTableAsync<CachedMeta>();
    }

    public async Task PopulateAsync(CatalogSnapshot snapshot)
    {
        // Wipe + rewrite in one transaction so a partial populate
        // can't leave us with stale meta over fresh rows (or vice
        // versa). Same atomicity guarantee as the IndexedDB readwrite
        // transaction in catalog-cache.js's populate().
        var conn = Db.GetConnection();
        await Task.Run(() => conn.RunInTransaction(() =>
        {
            conn.DeleteAll<CachedBook>();
            conn.DeleteAll<CachedBookIsbn>();
            conn.DeleteAll<CachedAuthor>();
            conn.DeleteAll<CachedSeries>();
            conn.DeleteAll<CachedMeta>();

            foreach (var book in snapshot.Books)
            {
                conn.Insert(new CachedBook
                {
                    Id = book.Id,
                    Title = book.Title,
                    PrimaryAuthor = book.PrimaryAuthor,
                    AllAuthorsJson = JsonSerializer.Serialize(book.AllAuthors),
                    Status = book.Status,
                    Rating = book.Rating,
                    SeriesId = book.SeriesId,
                    SeriesOrder = book.SeriesOrder,
                });
                foreach (var isbn in book.Isbns)
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
            }

            foreach (var author in snapshot.Authors)
            {
                conn.Insert(new CachedAuthor
                {
                    Id = author.Id,
                    Name = author.Name,
                    NameLower = author.Name.ToLowerInvariant(),
                    CanonicalId = author.CanonicalId,
                    BookCount = author.BookCount,
                });
            }

            foreach (var s in snapshot.Series)
            {
                conn.Insert(new CachedSeries
                {
                    Id = s.Id,
                    Name = s.Name,
                    Type = s.Type,
                    ExpectedCount = s.ExpectedCount,
                });
            }

            conn.Insert(new CachedMeta { Key = MetaKeyVersion, Value = snapshot.Version });
            conn.Insert(new CachedMeta { Key = MetaKeySyncedAt, Value = snapshot.SyncedAt.ToString("O") });
            conn.Insert(new CachedMeta { Key = MetaKeyBookCount, Value = snapshot.Books.Count.ToString() });
            conn.Insert(new CachedMeta { Key = MetaKeyAuthorCount, Value = snapshot.Authors.Count.ToString() });
        }));
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

        return new CacheMeta(
            Version: lookup.GetValueOrDefault(MetaKeyVersion),
            SyncedAt: syncedAt,
            BookCount: int.TryParse(lookup.GetValueOrDefault(MetaKeyBookCount), out var bc) ? bc : 0,
            AuthorCount: int.TryParse(lookup.GetValueOrDefault(MetaKeyAuthorCount), out var ac) ? ac : 0);
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
            book.SeriesOrder);
    }
}
