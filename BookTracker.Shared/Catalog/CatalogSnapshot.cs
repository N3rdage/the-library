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
    IReadOnlyList<SeriesSnapshot> Series,
    // Max Book.UpdatedAt across the rows in this response, or
    // SyncedAt when the response contains no books. Clients store
    // this and send it back as `?since=<token>` on the next refresh
    // to fetch only Books that changed since this snapshot — turning
    // refreshes into deltas instead of full reloads.
    //
    // Default of SyncedAt is for backwards-compat with any test or
    // caller constructing CatalogSnapshot positionally without the
    // new field.
    DateTime LatestUpdatedAt = default,
    // Book IDs that have been soft-deleted since the `since` token.
    // Populated only on delta calls (since != null); always empty on
    // a full snapshot. Bookshelf clients drop these rows from their
    // local cache when applying a delta. Default empty for back-compat
    // with positional CatalogSnapshot constructions.
    IReadOnlyList<int>? DeletedIds = null);

public record BookSnapshot(
    int Id,
    string Title,
    string PrimaryAuthor,
    // Every credited contributor on the Book (across all constituent Works
    // and roles — Author / Editor / Translator / Illustrator / etc.).
    // Field shape changed 2026-05-23 (snapshot v2): was IReadOnlyList<string>
    // of names only; now carries Role per row so non-Author contributions
    // (editors of reference works, translators of classical texts) round-
    // trip to consumers. Bookshelf cache consumers parsing the OLD shape
    // (a JSON array of strings) will need updating in the same version
    // window — see CatalogCache.PopulateAsync.
    IReadOnlyList<AuthorContribution> AllAuthors,
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
    string? CoverUrl = null,
    // Per-Edition detail for the Bookshelf enhanced-card view —
    // "which editions of this do I already own?". Each row is one
    // physical Edition (Hardcover / Paperback / MM / ...). Surfaced
    // in ScanPage's FoundFrame so the user can avoid double-buying.
    // Defaults to null for back-compat with positional BookSnapshot
    // constructions written before this field shipped; callers that
    // care should treat null as "no enrichment available" (the wire
    // could be an older server that doesn't project this).
    IReadOnlyList<EditionSnapshot>? Editions = null,
    // Per-Work detail — "what stories are in this compendium?".
    // Only meaningful for multi-Work compendiums; single-Work books
    // ship a one-element list which the UI hides. Defaults to null
    // for the same back-compat reason as Editions.
    IReadOnlyList<WorkSnapshot>? Works = null);

public record EditionSnapshot(
    int Id,
    // Nullable for pre-1974 / no-ISBN editions (matches Edition.Isbn).
    string? Isbn,
    // Format as the BookFormat enum's string name (Hardcover /
    // TradePaperback / MassMarketPaperback / ...). The DTO record
    // stays free of the BookTracker.Data dependency so Shared is
    // portable to non-Web consumers (Mobile, future projects).
    string Format,
    // Edition-level cover URL — distinct from BookSnapshot.CoverUrl
    // which is the Book-level default. Nullable when no cover has
    // been captured for this specific edition.
    string? CoverUrl);

public record WorkSnapshot(
    int Id,
    string Title,
    // Lowest-Order WorkAuthor (Role = Author) name. Same convention as
    // BookSnapshot.PrimaryAuthor but scoped to this specific Work — a
    // compendium's Asimov-Bradbury-King anthology shows the per-Work
    // attribution rather than the Book-wide rollup. Empty string when
    // the Work has no Author-role contributor (rare; e.g. a translated
    // classical text where only the translator is in our DB).
    string PrimaryAuthor,
    // Every credited contributor on this Work, with Role. New in
    // snapshot v2 (2026-05-23). Nullable for back-compat with older
    // positional WorkSnapshot constructions; new consumers should treat
    // null as "no enrichment from this server version" and fall back
    // to PrimaryAuthor alone.
    IReadOnlyList<AuthorContribution>? Contributors = null);

// One credited person on a Work, with the role they played. The Role
// field is the AuthorRole enum's string name (Author / Editor /
// Translator / Illustrator / Adaptor / Compiler / Foreword /
// Contributor) — the DTO record stays free of the BookTracker.Data
// dependency, so Role is exposed as a string rather than the enum
// type. Same convention as BookSnapshot.Status (string for BookStatus)
// and SeriesSnapshot.Type (string for SeriesType).
public record AuthorContribution(
    string Name,
    string Role)
{
    // Implicit conversion from string => AuthorContribution(name, "Author").
    // Matches the dominant case (Author is the default role across the system)
    // and keeps test fixture constructions concise — a Book's
    // `AllAuthors: ["Asimov"]` shorthand still compiles. Production code paths
    // that need an explicit non-Author role construct the record positionally;
    // there is no scenario where a string-typed name should silently become
    // an Editor / Translator contribution.
    public static implicit operator AuthorContribution(string name) =>
        new(name, "Author");
}

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
