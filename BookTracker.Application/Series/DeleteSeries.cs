using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>Hard-deletes a Series. Its member Books (and any WishlistItems that
/// referenced it) are detached by the database — both FKs are configured
/// <c>ON DELETE SET NULL</c>, so the rows survive with their series link (and,
/// for Books, the order) cleared. Idempotent: a no-op if the Series is already
/// gone, matching the old ViewModel.</summary>
public sealed record DeleteSeries(int SeriesId) : ICommand;

public sealed class DeleteSeriesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<DeleteSeries>
{
    public async Task HandleAsync(DeleteSeries command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var series = await db.Series.FindAsync([command.SeriesId], ct);
        if (series is null) return; // already deleted — idempotent

        db.Series.Remove(series);
        await db.SaveChangesAsync(ct);
    }
}
