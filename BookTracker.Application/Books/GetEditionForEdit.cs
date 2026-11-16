using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Read-model that prefills the Edit Edition dialog: the edition's fields, its
// book id and its publisher's name (null when it has none). The print date is
// raw (date + precision) so the dialog formats it the way the save parses it.
// Null when the edition is gone. Relocated from EditionFormDialogViewModel's
// tracked Include(Publisher) load (#264).
public sealed record GetEditionForEdit(int EditionId) : IQuery<EditionForEdit?>;

public record EditionForEdit(
    int Id,
    int BookId,
    string? Isbn,
    BookFormat Format,
    string? PublisherName,
    DateOnly? DatePrinted,
    DatePrecision DatePrintedPrecision,
    string? CoverUrl);

public sealed class GetEditionForEditHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetEditionForEdit, EditionForEdit?>
{
    public async Task<EditionForEdit?> HandleAsync(GetEditionForEdit query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Editions
            .AsNoTracking()
            .Where(e => e.Id == query.EditionId)
            .Select(e => new EditionForEdit(
                e.Id,
                e.BookId,
                e.Isbn,
                e.Format,
                e.Publisher == null ? null : e.Publisher.Name,
                e.DatePrinted,
                e.DatePrintedPrecision,
                e.CoverUrl))
            .FirstOrDefaultAsync(ct);
    }
}
