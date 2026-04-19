namespace BookTracker.Web.Services;

// One row in the title/author search results panel on the Add Book page.
// Pre-1974 books predate ISBN, so the search has to surface enough hint
// data for a human to disambiguate visually: cover, year, edition count.
// OpenLibraryUrl is the "show all editions" escape hatch when the user
// isn't sure which printing they're holding.
public record BookSearchCandidate(
    string WorkKey,
    string? Title,
    string? Author,
    int? FirstPublishYear,
    int? EditionCount,
    string? CoverUrl,
    string? OpenLibraryUrl);
