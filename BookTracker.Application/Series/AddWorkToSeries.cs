using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>Adds an existing Work to a Series from the series-management page,
/// landing it at the end of the running order (one past the current max). Routes
/// through the Work aggregate, which owns its series membership. Soft no-op if
/// the Work was deleted between picking it and saving.</summary>
public sealed record AddWorkToSeries(int SeriesId, int WorkId) : ICommand;

public sealed class AddWorkToSeriesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<AddWorkToSeries>
{
    public async Task HandleAsync(AddWorkToSeries command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var work = await db.Works.FindAsync([command.WorkId], ct);
        if (work is null) return; // gone between pick + save

        // Next slot = one past the highest order already in the series (1-based),
        // so the work appends to the end. Works with a null order count as 0, so
        // a series of all-unordered works yields 1 — matching the old VM calc.
        var maxOrder = await db.Works
            .Where(w => w.SeriesId == command.SeriesId)
            .MaxAsync(w => (int?)w.SeriesOrder, ct) ?? 0;

        work.AssignToSeries(command.SeriesId, maxOrder + 1, orderDisplay: null);
        await db.SaveChangesAsync(ct);
    }
}
