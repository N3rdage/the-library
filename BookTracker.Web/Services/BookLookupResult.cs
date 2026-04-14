namespace BookTracker.Web.Services;

public record BookLookupResult(
    string Isbn,
    string? Title,
    string? Author,
    IReadOnlyList<string> GenreCandidates,
    DateOnly? DatePrinted,
    string? CoverUrl,
    string Source);
