namespace BookTracker.Shared.Catalog;

// Wire-format DTOs for GET /api/catalog-snapshot, consumed by the
// /bookshop offline cache today and the BookTracker.Mobile MAUI app
// when that ships. See docs/mobile-app-design.md.
//
// `Status` and series `Type` are strings rather than the server-side
// enum types (BookStatus, SeriesType) — keeps this project free of
// the BookTracker.Data dependency. The JsonStringEnumConverter on the
// Web side already serialises enums as strings on the wire, so the
// shape doesn't change.

public record CatalogSnapshot(
    string Version,
    DateTime SyncedAt,
    IReadOnlyList<BookSnapshot> Books,
    IReadOnlyList<AuthorSnapshot> Authors,
    IReadOnlyList<SeriesSnapshot> Series);

public record BookSnapshot(
    int Id,
    string Title,
    string PrimaryAuthor,
    IReadOnlyList<string> AllAuthors,
    string Status,
    int Rating,
    IReadOnlyList<string> Isbns,
    // Series membership lives on Work, not Book. For multi-Work
    // compendiums the snapshot takes the first Work (by Work.Id),
    // matching the same convention used for PrimaryAuthor. Nullable
    // because most books aren't part of a series.
    int? SeriesId,
    int? SeriesOrder);

public record AuthorSnapshot(
    int Id,
    string Name,
    int CanonicalId,
    int BookCount);

public record SeriesSnapshot(
    int Id,
    string Name,
    string Type,
    int? ExpectedCount);
