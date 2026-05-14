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
[Table("book_editions")]
internal class CachedBookEdition
{
    // Edition.Id from the server — keeps a stable identity so future
    // upserts could diff at the Edition level. For PR 1 we wipe-and-
    // rewrite per Book on every populate / delta-upsert; the Id is
    // just useful for testability and future incremental shapes.
    [PrimaryKey] public int Id { get; set; }
    [Indexed] public int BookId { get; set; }
    public string? Isbn { get; set; }
    public string Format { get; set; } = string.Empty;
    public string? CoverUrl { get; set; }
}

// One row per Work belonging to a Book. Almost always one Work per
// Book; multi-Work rows are compendiums (anthologies, collections).
// Surfaced in ScanPage when Count > 1 — "Contains: 'Foundation',
// 'Second Foundation', ...".
[Table("book_works")]
internal class CachedBookWork
{
    [PrimaryKey] public int Id { get; set; }
    [Indexed] public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PrimaryAuthor { get; set; } = string.Empty;
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
