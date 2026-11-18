using BookTracker.Shared.Online;

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

    // Bookshelf's "Missing only": up to 100 of an author's works from Open
    // Library, most editions first. OwnedBookId is always null here; the
    // endpoint fills it from the library. Empty on a blank author or a
    // provider failure.
    Task<IReadOnlyList<OnlineAuthorWork>> GetAuthorWorksAsync(string author, CancellationToken ct);
}
