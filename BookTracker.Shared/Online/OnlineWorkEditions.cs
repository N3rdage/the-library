namespace BookTracker.Shared.Online;

// Wire-format DTOs for GET /api/online/work-editions: the other editions of
// an Open Library work, each flagged against the library, so Bookshelf can
// show which printings are already on the shelf. Lives here rather than in
// Web so Bookshelf deserialises the same shape the endpoint serialises.

/// <summary>A work's editions as Open Library lists them (at most 50).
/// <see cref="WorkKey"/> is the key as asked for (e.g. <c>/works/OL45804W</c>).</summary>
public record OnlineWorkEditions(string WorkKey, IReadOnlyList<OnlineEdition> Editions);

/// <summary>One Open Library edition. <see cref="Isbn"/> is the first ISBN-13,
/// else the first ISBN-10, cleaned to letters and digits; an edition without
/// one always has a title and a publisher. <see cref="PublishDate"/> and
/// <see cref="Format"/> are Open Library's free text as given.
/// <see cref="OwnedBookId"/> is the live library Book holding an Edition with
/// this ISBN, null when the library does not own it.</summary>
public record OnlineEdition(
    string? Isbn,
    string? Title,
    string? Publisher,
    string? PublishDate,
    string? Format,
    string? CoverUrl,
    int? OwnedBookId);
