using SQLite;

namespace BookTracker.Mobile.Cache.Models;

// Internal sqlite-net-pcl entities. Kept private from the public
// surface — callers see BookSnapshot / AuthorSnapshot / SeriesSnapshot
// from BookTracker.Shared instead.

[Table("books")]
internal class CachedBook
{
    [PrimaryKey] public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Lowercased title for case-insensitive substring search.
    /// Mirrors the <see cref="CachedAuthor.NameLower"/> pattern so
    /// SearchBooksByTitleAsync's LIKE predicate sits on an indexed
    /// column. Recomputed on every insert / upsert; do not write
    /// directly from callers.</summary>
    [Indexed] public string TitleLower { get; set; } = string.Empty;
    public string PrimaryAuthor { get; set; } = string.Empty;
    /// <summary>JSON-encoded list of all credited author names.
    /// sqlite-net-pcl has no native collection mapping; we serialise
    /// on insert and parse on read.</summary>
    public string AllAuthorsJson { get; set; } = "[]";
    /// <summary>Status as string (the wire-format DTO already uses
    /// string, not the BookStatus enum, to keep Shared free of
    /// BookTracker.Data).</summary>
    public string Status { get; set; } = string.Empty;
    public int Rating { get; set; }
    public int? SeriesId { get; set; }
    public int? SeriesOrder { get; set; }
    /// <summary>Human-facing order label overriding <see cref="SeriesOrder"/>
    /// when the position isn't a plain integer ("4.5" interquel, "1A"). Null
    /// for ordinary integer orders. Also the signal that a given
    /// <see cref="SeriesOrder"/> is a floored/shared slot rather than a true
    /// numbered volume — gap detection skips display-only rows so an interquel
    /// doesn't mask a genuinely-missing numbered book. NULL is the correct
    /// default for legacy rows (no interquel), so no InitAsync backfill is
    /// needed (mirrors the EditionNumber int? case).</summary>
    public string? SeriesOrderDisplay { get; set; }
    /// <summary>Remote cover URL from the snapshot (server-side
    /// Book.DefaultCoverArtUrl). Null when the Book has no cover set.
    /// Populated from BookSnapshot.CoverUrl; reset on every
    /// PopulateAsync (the wipe-and-rewrite pattern).</summary>
    public string? CoverUrl { get; set; }
    /// <summary>Local filesystem path of the cached cover JPEG, or null
    /// if the cover hasn't been fetched yet (or the fetch failed).
    /// Written by EnsureCoverCachedAsync after the download + resize
    /// succeeds. Reset on every PopulateAsync — lazy re-fetch on first
    /// display after a snapshot refresh.</summary>
    public string? CoverPath { get; set; }
}

// One row per (BookId, Isbn). Many-to-one with books. Indexed on
// Isbn so ISBN lookup is a B-tree hit, mirroring the IndexedDB
// multiEntry index on `isbns` in catalog-cache.js.
[Table("book_isbns")]
internal class CachedBookIsbn
{
    [PrimaryKey, AutoIncrement] public int RowId { get; set; }
    public int BookId { get; set; }
    [Indexed] public string Isbn { get; set; } = string.Empty;
}

// One row per Edition of a Book. Backs the enhanced ScanPage "which
// editions do I already own?" view — Hardcover, Paperback, MM, etc.
// Indexed on BookId so the per-Book lookup is a B-tree hit.
//
// Surrogate AutoIncrement PK + natural EditionId column — matches the
// CachedBookIsbn pattern. Edition is 1:N with Book on the server side
// (an Edition belongs to one Book), so a natural-Id PK wouldn't collide
// today, but the symmetric shape with CachedBookWork (which DOES need
// the surrogate to avoid the shared-Work collision) is safer to maintain.
[Table("book_editions")]
internal class CachedBookEdition
{
    [PrimaryKey, AutoIncrement] public int RowId { get; set; }
    [Indexed] public int BookId { get; set; }
    public int EditionId { get; set; }
    public string? Isbn { get; set; }
    public string Format { get; set; } = string.Empty;
    public string? CoverUrl { get; set; }
    /// <summary>Revision number (1 = first edition, 2 = revised second,
    /// etc.). Nullable on the wire and here. Drives the "3rd ed."
    /// suffix on ScanPage's per-Edition row. NULL is the correct
    /// default for legacy rows — no Init backfill needed (unlike the
    /// string columns which would blow up Deserialize).</summary>
    public int? EditionNumber { get; set; }
}

// One row per (Book, Work) pair — Book ↔ Work is many-to-many on the
// server (a Work like Lovecraft's "Call of Cthulhu" can appear in
// multiple anthologies you own). Surrogate AutoIncrement PK so the
// same Work.Id can repeat across rows under different BookIds without
// hitting a UNIQUE constraint. Indexed on BookId for the per-Book
// lookup in GetBookEnrichedDetailAsync.
//
// Surfaced in ScanPage when the Book has more than one Work —
// "Contains: 'Foundation', 'Second Foundation', ...". Single-Work
// books still get a row each but the UI hides the section.
[Table("book_works")]
internal class CachedBookWork
{
    [PrimaryKey, AutoIncrement] public int RowId { get; set; }
    [Indexed] public int BookId { get; set; }
    public int WorkId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PrimaryAuthor { get; set; } = string.Empty;
    /// <summary>This Work's display position within its Book (server's
    /// BookWork.Order). Read back OrderBy(Order).ThenBy(WorkId) so legacy rows
    /// that predate the column (all 0 after the ALTER) fall back to WorkId order
    /// — no InitAsync backfill needed. Reordered books re-sync with real values
    /// via the delta (a reorder bumps Book.UpdatedAt).</summary>
    public int Order { get; set; }
    /// <summary>JSON-encoded list of every credited contributor on the
    /// Work with their Role (Author / Editor / Translator / Illustrator
    /// / etc.). Mirrors CachedBook.AllAuthorsJson but scoped per-Work
    /// so a compendium's per-story attribution survives the cache
    /// round-trip. Empty list (`"[]"`) for legacy rows + back-compat
    /// with older servers that don't ship WorkSnapshot.Contributors.</summary>
    public string ContributorsJson { get; set; } = "[]";
}

[Table("authors")]
internal class CachedAuthor
{
    [PrimaryKey] public int Id { get; set; }
    /// <summary>Lowercased for case-insensitive substring search.
    /// Mirrors `name.toLowerCase().includes(query)` in
    /// catalog-cache.js's searchAuthors.</summary>
    [Indexed] public string NameLower { get; set; } = string.Empty;
    /// <summary>Original-case name returned to callers.</summary>
    public string Name { get; set; } = string.Empty;
    [Indexed] public int CanonicalId { get; set; }
    public int BookCount { get; set; }
}

[Table("series")]
internal class CachedSeries
{
    [PrimaryKey] public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? ExpectedCount { get; set; }
}

[Table("meta")]
internal class CachedMeta
{
    [PrimaryKey] public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

// One row per wishlist item from the server snapshot. Same Id as the
// server's WishlistItem.Id so local-bought references stay stable
// across catalog refreshes. CoverUrl + Priority + DateAdded land here
// as the WishlistPage display fields; the per-ISBN scan-flag lookup
// goes via CachedWishlistItemIsbn (1:N).
[Table("wishlist_items")]
internal class CachedWishlistItem
{
    [PrimaryKey] public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? CoverUrl { get; set; }
    public int? SeriesId { get; set; }
    public int? SeriesOrder { get; set; }
    public DateTime DateAdded { get; set; }
}

// One row per (WishlistItemId, Isbn). Indexed on Isbn so the ScanPage
// scan-flag lookup is a B-tree seek. The server's WishlistSnapshotService
// already unions legacy + per-row ISBNs into one list; the cache just
// stores the flat shape.
[Table("wishlist_item_isbns")]
internal class CachedWishlistItemIsbn
{
    [PrimaryKey, AutoIncrement] public int RowId { get; set; }
    [Indexed] public int WishlistItemId { get; set; }
    [Indexed] public string Isbn { get; set; } = string.Empty;
}

// Local-only "bought" toggle state. Survives catalog refreshes —
// orphan-tolerant: if a server wishlist row goes away (Drew captured
// the book), the bought-local entry becomes harmless (the join
// against CachedWishlistItem yields nothing). PR D's WishlistPage
// filters out any CachedWishlistItem whose Id appears in this table.
[Table("wishlist_bought_local")]
internal class WishlistBoughtLocal
{
    [PrimaryKey] public int WishlistItemId { get; set; }
    public DateTime MarkedAt { get; set; }
}
