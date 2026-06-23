using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>Removes a Work from its Series (the series-management page). Routes
/// through the Work aggregate's ClearSeries, which drops the series link, the
/// order, AND the display label — the old ViewModel left a dangling
/// SeriesOrderDisplay (e.g. "4.5") behind on a removed work; clearing it here is
/// the deliberate consistency fix. Soft no-op if the Work is already gone.</summary>
public sealed record RemoveWorkFromSeries(int WorkId) : ICommand;

public sealed class RemoveWorkFromSeriesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RemoveWorkFromSeries>
{
    public async Task HandleAsync(RemoveWorkFromSeries command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var work = await db.Works.FindAsync([command.WorkId], ct);
        if (work is null) return;

        work.ClearSeries();
        await db.SaveChangesAsync(ct);
    }
}
