using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

// One book as a row of the Series edit page's table (title, author display,
// order). The page reads it after AddBookToSeries to pick up the order the
// handler assigned. Null when the book is gone. Relocated from
// SeriesEditViewModel's tracked re-read after the add (#265).
public sealed record GetSeriesBookRow(int BookId) : IQuery<SeriesBookRow?>;

public sealed class GetSeriesBookRowHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetSeriesBookRow, SeriesBookRow?>
{
    public async Task<SeriesBookRow?> HandleAsync(GetSeriesBookRow query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books
            .AsNoTracking()
            .Where(b => b.Id == query.BookId)
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.SeriesOrder,
                b.SeriesOrderDisplay,
                AuthorsPerWork = b.Works
                    .Select(w => w.WorkAuthors
                        .Where(wa => wa.Role == AuthorRole.Author)
                        .OrderBy(wa => wa.Order)
                        .Select(wa => wa.Author.Name)
                        .ToList())
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (book is null) return null;

        return new SeriesBookRow(
            book.Id, book.Title, SeriesBookAuthors.Display(book.AuthorsPerWork), book.SeriesOrder, book.SeriesOrderDisplay);
    }
}
