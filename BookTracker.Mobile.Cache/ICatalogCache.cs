using BookTracker.Shared.Catalog;
using BookTracker.Shared.Wishlist;

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
    /// partial populate can't leave the DB half-shrunken. Use this
    /// for first-load (no stored watermark) and full-reset paths
    /// (server <c>Version</c> mismatch — deploy invalidated cache).</summary>
    Task PopulateAsync(CatalogSnapshot snapshot);

    /// <summary>Merges a delta-shaped snapshot into the existing
    /// cache. Upserts incoming Books (preserves <c>CoverPath</c> when
    /// <c>CoverUrl</c> hasn't changed, clears it when it has so
    /// <see cref="EnsureCoverCachedAsync"/> re-fetches the thumbnail).
    /// Wipes-and-rewrites Authors + Series (server always full-lists
    /// them regardless of <c>?since=</c>). Processes
    /// <c>snapshot.DeletedIds</c> as DELETEs against the local rows.
    /// Updates the watermark meta (<c>latestUpdatedAt</c>) so the
    /// next refresh can send it as <c>?since=</c>.
    ///
    /// Atomic via a single SQLite transaction. Caller is responsible
    /// for falling back to <see cref="PopulateAsync"/> on a
    /// server-version mismatch.</summary>
    Task ApplyDeltaAsync(CatalogSnapshot snapshot);

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

    /// <summary>Substring search on book title (case-insensitive).
    /// Mirrors <see cref="SearchAuthorsAsync"/> shape for the "I'm
    /// pretty sure I have a book called <i>something Mountain</i>"
    /// use case. Returns at most <paramref name="limit"/> results
    /// (default 20 when limit ≤ 0) sorted alphabetically by title.
    /// Empty/whitespace query returns no results.</summary>
    Task<IReadOnlyList<BookSnapshot>> SearchBooksByTitleAsync(string query, int limit);

    /// <summary>Returns one entry per Series with a finite
    /// <c>ExpectedCount</c> where the user owns at least one book but
    /// not the full set. Skipped: series with no ExpectedCount
    /// (open-ended), series with zero owned books (user hasn't started
    /// the series), series with no gaps (complete). Sorted
    /// alphabetically by series name. Backs the Bookshelf "Series
    /// gaps" page — "in the bookshop, what am I missing?"</summary>
    Task<IReadOnlyList<SeriesGap>> GetSeriesGapsAsync();

    /// <summary>Reads the per-Book Editions + Works detail for the
    /// enhanced ScanPage view. Returns null when the Book isn't in
    /// the cache. Editions list is sorted by Format then Isbn; Works
    /// list is sorted by Work.Id (mirrors the server's "first Work
    /// by Id" convention). Empty inner lists are possible when the
    /// server hasn't shipped the enriched fields yet (back-compat).</summary>
    Task<BookEnrichedDetail?> GetBookEnrichedDetailAsync(int bookId);

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

    /// <summary>Wipes existing wishlist rows and rewrites from the
    /// snapshot. Same wipe-and-rewrite shape as Authors / Series on the
    /// catalog snapshot — the wishlist is small enough that delta-sync
    /// adds complexity without payoff. WishlistBoughtLocal entries are
    /// preserved across the rewrite (orphan-tolerant — entries whose
    /// server row is gone become harmless no-ops).</summary>
    Task PopulateWishlistAsync(WishlistSnapshot snapshot);

    /// <summary>Returns the wishlist rows the user hasn't tapped
    /// "bought" yet. Sort is server-side (priority desc, then date).
    /// Backs the Bookshelf WishlistPage display. Cover URLs come back
    /// as-is — caller fetches the bytes via HttpClient (lightweight,
    /// not via the per-Book disk cache used for the catalog).</summary>
    Task<IReadOnlyList<WishlistItemSnapshot>> GetWishlistAsync();

    /// <summary>Marks a wishlist row as locally bought. Survives catalog
    /// refresh — entries whose server row is gone (Drew captured the
    /// book) become harmless. <see cref="GetWishlistAsync"/> excludes
    /// any row whose Id is in this table.</summary>
    Task MarkBoughtLocallyAsync(int wishlistItemId);

    /// <summary>Undo a local "bought" mark — the row reappears in
    /// <see cref="GetWishlistAsync"/>. Idempotent: no-op if the row
    /// wasn't bought-marked.</summary>
    Task UnmarkBoughtLocallyAsync(int wishlistItemId);

    /// <summary>True when the given ISBN matches any wishlisted ISBN
    /// (across all rows, including legacy single-Isbn rows and the new
    /// per-row Isbns from PR B's WishlistItemIsbn table — the server
    /// unions them at the snapshot layer). Bought-local rows are
    /// excluded so a book the user just marked bought doesn't keep
    /// flagging on subsequent scans. Backs ScanPage's wishlist badge.</summary>
    Task<bool> IsWishlistedIsbnAsync(string isbn);
}

/// <summary>One series the user owns part of but not all of. Backs the
/// Bookshelf "Series gaps" page — at a glance, the user can see what's
/// missing while standing in a bookshop. <see cref="MissingOrders"/>
/// lists the integer SeriesOrder values from 1..ExpectedCount that the
/// user doesn't own (e.g. <c>[2, 6]</c> means missing #2 and #6).
/// Books with null SeriesOrder count toward OwnedCount but don't fill
/// any specific slot — they neither help nor hurt the gap calculation.</summary>
public record SeriesGap(
    int SeriesId,
    string SeriesName,
    string SeriesType,
    int ExpectedCount,
    int OwnedCount,
    IReadOnlyList<int> MissingOrders);

/// <summary>Per-Book enriched detail surfaced in Bookshelf's ScanPage
/// FoundFrame — the "which Editions of this do I own, and what Works
/// does it contain?" view. Editions + Works lists are always non-null;
/// callers check Count and hide the corresponding section when ≤ 1
/// (multi-edition / multi-work books are the cases that benefit from
/// the enriched display).</summary>
public record BookEnrichedDetail(
    IReadOnlyList<EditionSnapshot> Editions,
    IReadOnlyList<WorkSnapshot> Works);

public record CacheMeta(
    string? Version,
    DateTime? SyncedAt,
    int BookCount,
    int AuthorCount,
    // Server-side max(Book.UpdatedAt | DeletedAt) at the moment of
    // the last successful PopulateAsync / ApplyDeltaAsync. Caller
    // sends this back as <c>?since=</c> on the next refresh so the
    // server returns only changed Books + tombstones. Null when no
    // snapshot has been applied yet (or it was stored before the
    // server started shipping the field — back-compat).
    DateTime? LatestUpdatedAt = null);
