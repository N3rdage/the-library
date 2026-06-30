using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>
/// Eagerly find-or-create a Series by name and return its id. Dispatched when
/// the user accepts a "new series" suggestion (TD-15a) so the row exists at the
/// accept gesture, letting the Book / Bulk Add save attach by id instead of
/// find-or-creating during the aggregate save. Idempotent — an existing name
/// returns its id; a blank name is a no-op returning null.
///
/// Find-or-create via <see cref="SeriesResolver"/> (default Type=Series, no
/// expected count), so name handling + the ExpectedCount/Type invariant live in
/// one place. This is deliberately distinct from the strict <c>CreateSeries</c>
/// command (full fields + duplicate rejection, used by the /series create form):
/// accepting a suggestion must be idempotent, not throw on a name that already
/// exists. Mirrors <c>CreateAuthor</c> / <c>CreatePublisher</c>.
/// </summary>
public sealed record EnsureSeries(string? Name) : ICommand<int?>;

public sealed class EnsureSeriesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<EnsureSeries, int?>
{
    public async Task<int?> HandleAsync(EnsureSeries command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var series = await SeriesResolver.ResolveAsync(db, command.Name, ct);
        if (series is null) return null;
        await db.SaveChangesAsync(ct);
        return series.Id;
    }
}
