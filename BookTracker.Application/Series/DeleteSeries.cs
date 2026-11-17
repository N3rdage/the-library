using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>Hard-deletes a Series. Each member Book is loaded and cleared through
/// <c>Book.ClearSeries()</c> (nulling <c>SeriesId</c>, <c>SeriesOrder</c> and
/// <c>SeriesOrderDisplay</c>) in the same save as the removal, so the Books survive
/// outside any series and <c>BookUpdatedAtInterceptor</c> bumps their
/// <c>UpdatedAt</c> for the Bookshelf delta sync. WishlistItems that referenced the
/// Series are detached by the database's <c>ON DELETE SET NULL</c>. Idempotent: a
/// no-op if the Series is already gone.</summary>
public sealed record DeleteSeries(int SeriesId) : ICommand;

public sealed class DeleteSeriesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<DeleteSeries>
{
    public async Task HandleAsync(DeleteSeries command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var series = await db.Series.FindAsync([command.SeriesId], ct);
        if (series is null) return; // already deleted — idempotent

        var members = await db.Books
            .Where(b => b.SeriesId == command.SeriesId)
            .ToListAsync(ct);
        foreach (var book in members)
        {
            book.ClearSeries();
        }

        db.Series.Remove(series);
        await db.SaveChangesAsync(ct);
    }
}
