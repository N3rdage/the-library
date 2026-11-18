namespace BookTracker.Shared.Online;

// Wire-format DTOs for GET /api/online/isbn/{isbn}: what the providers know
// about an ISBN the phone's cache does not hold, plus whether the library owns
// that work in another printing. Lives here rather than in Web so Bookshelf
// deserialises the same shape the endpoint serialises.

/// <summary>A provider answer for one ISBN. <see cref="Source"/> names the
/// provider that answered; <see cref="Owned"/> is null when the library holds
/// neither this edition nor its work. <see cref="WorkKey"/> is Open Library's
/// work key (e.g. <c>/works/OL45804W</c>), which the phone passes to
/// <c>/api/online/work-editions</c>; null when another provider answered.</summary>
public record OnlineIsbnLookup(
    string Isbn,
    string? Title,
    string? Subtitle,
    string? Author,
    string? Publisher,
    string? CoverUrl,
    int? PublishedYear,
    string Source,
    OwnedWorkMatch? Owned,
    string? WorkKey);

/// <summary>The owned Book behind an online lookup. <see cref="SameEdition"/>
/// separates "you own this printing" from "you own this work in another
/// printing", which the phone words differently.</summary>
public record OwnedWorkMatch(int BookId, string BookTitle, bool SameEdition);
