namespace BookTracker.Web.Services;

public interface IBookLookupService
{
    Task<BookLookupResult?> LookupByIsbnAsync(string isbn, CancellationToken ct);

    // Used for pre-ISBN books (pre-1974). Returns up to ~10 work-level
    // candidates from Open Library so the user can pick the right title
    // visually. Title and author are both optional but at least one must
    // be non-empty.
    Task<IReadOnlyList<BookSearchCandidate>> SearchByTitleAuthorAsync(
        string? title, string? author, CancellationToken ct);
}
