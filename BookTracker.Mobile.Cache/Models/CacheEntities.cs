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
