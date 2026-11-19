namespace BookTracker.Shared.Online;

// Wire-format DTOs for GET /api/online/author-works: an author's bibliography
// from Open Library with each work flagged against the library, so Bookshelf
// can show "Missing only". Lives here rather than in Web so Bookshelf
// deserialises the same shape the endpoint serialises.

/// <summary>An author's works as Open Library lists them, most editions
/// first. <see cref="Author"/> is the name as asked for (trimmed).</summary>
public record OnlineAuthorWorks(string Author, IReadOnlyList<OnlineAuthorWork> Works);

/// <summary>One Open Library work. <see cref="WorkKey"/> is Open Library's
/// key (e.g. <c>/works/OL45804W</c>); <see cref="OwnedBookId"/> is the live
/// library Book holding a Work of the same normalised title by the same
/// author (aliases rolled up), null when the library does not own it.</summary>
public record OnlineAuthorWork(
    string WorkKey,
    string? Title,
    int? FirstPublishYear,
    string? CoverUrl,
    int? EditionCount,
    int? OwnedBookId);
