using BookTracker.Shared.Catalog;

namespace BookTracker.Mobile.Cache;

// Mirrors the JS surface in
// BookTracker.Web/wwwroot/js/catalog-cache.js. The PWA's
// IndexedDB-backed cache and this SQLite-backed cache serve the same
// killer use cases — ISBN lookup + author search + author-drilldown
// — with the same shape so a cross-platform port is mechanical.
// See docs/mobile-app-design.md.
public interface ICatalogCache
{
    /// <summary>Opens (or creates) the SQLite DB at the given path.
    /// Idempotent; safe to call on every app launch.</summary>
    Task InitAsync(string dbPath);

    /// <summary>Wipes existing cache rows and inserts the snapshot
    /// from scratch. Atomic via a single SQLite transaction — a
    /// partial populate can't leave the DB half-shrunken.</summary>
    Task PopulateAsync(CatalogSnapshot snapshot);

    /// <summary>Finds the book with the given ISBN, or null if not
    /// in the cache. Uses the book_isbns index — sub-millisecond
    /// at the 3000+ books target.</summary>
    Task<BookSnapshot?> LookupByIsbnAsync(string isbn);

    /// <summary>All books credited to the given canonical author OR
    /// any of its aliases (canonical/alias rollup via the JS
    /// implementation's logic — book.allAuthors intersect with
    /// names matching this canonicalId).</summary>
    Task<IReadOnlyList<BookSnapshot>> LookupByAuthorAsync(int canonicalId);

    /// <summary>Substring search on author name (canonical OR alias),
    /// deduped by canonicalId. Mirrors catalog-cache.js: typing
    /// "Bachman" finds King via the alias row.</summary>
    Task<IReadOnlyList<AuthorSnapshot>> SearchAuthorsAsync(string query, int limit);

    /// <summary>Stored sync metadata — version, syncedAt, bookCount,
    /// authorCount. Null if the cache has never been populated.</summary>
    Task<CacheMeta?> GetMetaAsync();

    /// <summary>Ensures a local copy of the Book's cover exists on disk
    /// at &lt;covers-dir&gt;/{bookId}.jpg, downloading + resizing it on
    /// first call. Returns the local path on success, or null if the
    /// book isn't cached, has no CoverUrl, or the download/resize
    /// failed (offline, 404, malformed bytes). Lazy-on-load: callers
    /// invoke when about to render the cover. Idempotent — subsequent
    /// calls short-circuit on the existing file.
    ///
    /// HttpClient is supplied by the caller rather than constructed
    /// here so the platform layer (Bookshelf MAUI) keeps lifetime +
    /// auth + retry concerns in one place, and tests can pass a
    /// mocked HttpMessageHandler.
    /// </summary>
    Task<string?> EnsureCoverCachedAsync(int bookId, HttpClient http, CancellationToken ct = default);
}

public record CacheMeta(
    string? Version,
    DateTime? SyncedAt,
    int BookCount,
    int AuthorCount);
