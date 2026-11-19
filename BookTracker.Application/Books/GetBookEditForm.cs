using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Read-model that prefills the Edit book details dialog: the Book-level fields
// it edits, with the series order raw (SeriesOrder sort key + SeriesOrderDisplay
// override) so the dialog formats it the way the save parses it. Null when the
// book is gone or soft-deleted (the global query filter applies). Relocated
// from BookEditDialogViewModel's tracked FindAsync (#264).
public sealed record GetBookEditForm(int BookId) : IQuery<BookEditForm?>;

public record BookEditForm(
    int Id,
    string Title,
    BookCategory Category,
    string? DefaultCoverArtUrl,
    int? SeriesId,
    int? SeriesOrder,
    string? SeriesOrderDisplay);

public sealed class GetBookEditFormHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetBookEditForm, BookEditForm?>
{
    public async Task<BookEditForm?> HandleAsync(GetBookEditForm query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Books
            .AsNoTracking()
            .Where(b => b.Id == query.BookId)
            .Select(b => new BookEditForm(
                b.Id, b.Title, b.Category, b.DefaultCoverArtUrl,
                b.SeriesId, b.SeriesOrder, b.SeriesOrderDisplay))
            .FirstOrDefaultAsync(ct);
    }
}
