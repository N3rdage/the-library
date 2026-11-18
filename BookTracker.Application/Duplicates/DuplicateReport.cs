using BookTracker.Data.Models;

namespace BookTracker.Application.Duplicates;

// Read-model for the /duplicates page: candidate pairs per entity type, each
// carrying the dismissal that hides it from the active list (null when active).
public record DuplicateReport(
    IReadOnlyList<AuthorDuplicatePair> Authors,
    IReadOnlyList<WorkDuplicatePair> Works,
    IReadOnlyList<BookDuplicatePair> Books,
    IReadOnlyList<EditionDuplicatePair> Editions);

public record DismissalInfo(int IgnoredDuplicateId, DateTime DismissedAt, string? Note);

public record DuplicateAuthorView(int Id, string Name, int WorkCount, int? CanonicalAuthorId, string? CanonicalName);
public record AuthorDuplicatePair(DuplicateAuthorView Lower, DuplicateAuthorView Higher, DismissalInfo? Dismissed, string MatchReason);

public record DuplicateWorkView(int Id, string Title, string? Subtitle, string AuthorName, int BookCount, int? FirstPublishedYear);
public record WorkDuplicatePair(DuplicateWorkView Lower, DuplicateWorkView Higher, DismissalInfo? Dismissed, string MatchReason);

public record DuplicateBookView(int Id, string Title, string? AuthorName, int EditionCount, int CopyCount, DateTime DateAdded);
public record BookDuplicatePair(DuplicateBookView Lower, DuplicateBookView Higher, DismissalInfo? Dismissed, string MatchReason);

public record DuplicateEditionView(int Id, string? Isbn, BookFormat Format, string? PublisherName, DateOnly? DatePrinted, int CopyCount, int BookId, string BookTitle);
public record EditionDuplicatePair(DuplicateEditionView Lower, DuplicateEditionView Higher, DismissalInfo? Dismissed, string MatchReason);
