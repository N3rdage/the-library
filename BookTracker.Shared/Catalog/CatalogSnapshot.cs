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
    int? SeriesOrder,
    // Book-level default cover URL. Nullable — pre-1974 / no-ISBN books
    // and any Book that hasn't had a cover set yet ship without one.
    // Bookshelf's cover-cache layer downloads on first display, resizes
    // to a 200px-long-edge JPEG, stores locally, then serves from disk
    // on subsequent loads. Bookcase's /bookshop ignores this field
    // (deep-links into the app for visuals).
    //
    // Default of `null` is for backwards-compat with existing positional
    // BookSnapshot constructions in tests and other callers; new code
    // should set it explicitly.
    string? CoverUrl = null);

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
