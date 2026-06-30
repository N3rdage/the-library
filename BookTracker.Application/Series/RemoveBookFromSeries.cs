using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>Removes a Book from its Series (the series-management page). Routes
/// through the Book aggregate's ClearSeries, which drops the series link, the
/// order, AND the display label (e.g. "4.5") so nothing dangles behind a removed
/// book. Soft no-op if the Book is already gone.</summary>
public sealed record RemoveBookFromSeries(int BookId) : ICommand;

public sealed class RemoveBookFromSeriesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RemoveBookFromSeries>
{
    public async Task HandleAsync(RemoveBookFromSeries command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct);
        if (book is null) return;

        book.ClearSeries();
        await db.SaveChangesAsync(ct);
    }
}
