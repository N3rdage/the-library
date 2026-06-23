using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
// The feature folder is Series/, so the namespace below collides with the
// Series *type* (unlike Works/Work, where the plural folder dodges it). Alias
// the entity wherever its static surface is needed.
using SeriesAggregate = BookTracker.Data.Models.Series;

namespace BookTracker.Application.Series;

/// <summary>Creates a new Series (or Collection). The ExpectedCount/Type
/// invariant is enforced by the aggregate; name uniqueness by the handler.
/// Returns the new Series' id. Throws <see cref="DomainRuleException"/> on a
/// blank name or a duplicate name.</summary>
public sealed record CreateSeries(
    string Name,
    string? Author,
    SeriesType Type,
    int? ExpectedCount,
    string? Description) : ICommand<int>;

public sealed class CreateSeriesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<CreateSeries, int>
{
    public async Task<int> HandleAsync(CreateSeries command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await SeriesNameGuard.EnsureUniqueAsync(db, command.Name, excludeId: null, ct);

        var series = SeriesAggregate.Create(
            command.Name, command.Author, command.Type, command.ExpectedCount, command.Description);
        db.Series.Add(series);
        await db.SaveChangesAsync(ct);
        return series.Id;
    }
}
