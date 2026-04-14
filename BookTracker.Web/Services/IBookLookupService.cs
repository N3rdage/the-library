namespace BookTracker.Web.Services;

public interface IBookLookupService
{
    Task<BookLookupResult?> LookupByIsbnAsync(string isbn, CancellationToken ct);
}
