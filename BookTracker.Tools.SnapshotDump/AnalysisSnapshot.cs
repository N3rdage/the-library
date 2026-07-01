using BookTracker.Data.Models;

namespace BookTracker.Tools.SnapshotDump;

// DTO shape for the JSON file uploaded to the Claude "BookTracker
// Analysis" Project. Distinct from BookTracker.Shared's CatalogSnapshot
// (which is the slim ISBN-keyed Bookshelf wire format) — this is the
// richer, analysis-oriented shape: includes Notes, Rating, soft-delete-
// excluded Books, full Author rollup graph, and tags.
//
// Editions nest inside Books because the relationship is dense and
// rarely-shared. Authors / Genres / Series / Publishers / Tags are
// top-level lists referenced from Books / Works by ID — keeps the JSON
// readable when entities are reused.
//
// Field semantics: see docs/DATA-DICTIONARY.md.

public sealed record CatalogAnalysisSnapshot(
    DateTime GeneratedAtUtc,
    string Source,
    int BookCount,
    int WorkCount,
    int AuthorCount,
    IReadOnlyList<BookAnalysis> Books,
    IReadOnlyList<WorkAnalysis> Works,
    IReadOnlyList<AuthorAnalysis> Authors,
    IReadOnlyList<GenreAnalysis> Genres,
    IReadOnlyList<SeriesAnalysis> Series,
    IReadOnlyList<PublisherAnalysis> Publishers,
    IReadOnlyList<TagAnalysis> Tags,
    IReadOnlyList<WishlistItemAnalysis> WishlistItems);

public sealed record BookAnalysis(
    int Id,
    string Title,
    BookCategory Category,
    BookStatus Status,
    int Rating,
    string? Notes,
    DateTime DateAdded,
    DateTime UpdatedAt,
    string? DefaultCoverArtUrl,
    int? SeriesId,
    int? SeriesOrder,
    string? SeriesOrderDisplay,
    IReadOnlyList<EditionAnalysis> Editions,
    IReadOnlyList<int> WorkIds,
    IReadOnlyList<string> TagNames);

public sealed record EditionAnalysis(
    int Id,
    string? Isbn,
    BookFormat Format,
    DateOnly? DatePrinted,
    DatePrecision DatePrintedPrecision,
    string? CoverUrl,
    bool IsUserSupplied,
    string? PublisherName,
    IReadOnlyList<CopyAnalysis> Copies);

public sealed record CopyAnalysis(
    int Id,
    BookCondition Condition,
    DateTime? DateAcquired,
    string? Notes);

public sealed record WorkAnalysis(
    int Id,
    string Title,
    string? Subtitle,
    DateOnly? FirstPublishedDate,
    DatePrecision FirstPublishedDatePrecision,
    IReadOnlyList<WorkAuthorRef> Authors,
    IReadOnlyList<string> GenreNames,
    IReadOnlyList<int> BookIds);

public sealed record WorkAuthorRef(int AuthorId, string Name, int Order);

public sealed record AuthorAnalysis(
    int Id,
    string Name,
    int? CanonicalAuthorId,
    string? CanonicalAuthorName,
    IReadOnlyList<int> AliasIds);

public sealed record GenreAnalysis(
    int Id,
    string Name,
    int? ParentGenreId,
    string? ParentGenreName);

public sealed record SeriesAnalysis(
    int Id,
    string Name,
    string? Author,
    SeriesType Type,
    int? ExpectedCount,
    string? Description);

public sealed record PublisherAnalysis(int Id, string Name);

public sealed record TagAnalysis(int Id, string Name);

public sealed record WishlistItemAnalysis(
    int Id,
    string Title,
    string Author,
    WishlistPriority Priority,
    decimal? Price,
    DateTime DateAdded,
    string? Isbn,
    int? SeriesId,
    int? SeriesOrder);
