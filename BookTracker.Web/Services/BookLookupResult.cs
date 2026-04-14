namespace BookTracker.Web.Services;

public record BookLookupResult(
    string Isbn,
    string? Title,
    string? Subtitle,
    string? Author,
    string? Publisher,
    IReadOnlyList<string> GenreCandidates,
    DateOnly? DatePrinted,
    string? CoverUrl,
    string Source);
