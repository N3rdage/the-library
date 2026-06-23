using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>Updates an existing Series' editable details (name, author, type,
/// expected count, description). The ExpectedCount/Type invariant is enforced by
/// the aggregate; name uniqueness by the handler (excluding this row). Throws
/// <see cref="NotFoundException"/> if the Series is gone,
/// <see cref="DomainRuleException"/> on a blank or duplicate name.</summary>
public sealed record UpdateSeries(
    int SeriesId,
    string Name,
    string? Author,
    SeriesType Type,
    int? ExpectedCount,
    string? Description) : ICommand;

public sealed class UpdateSeriesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<UpdateSeries>
{
    public async Task HandleAsync(UpdateSeries command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var series = await db.Series.FindAsync([command.SeriesId], ct)
            ?? throw new NotFoundException($"Series {command.SeriesId} not found.");

        await SeriesNameGuard.EnsureUniqueAsync(db, command.Name, excludeId: command.SeriesId, ct);

        series.UpdateDetails(
            command.Name, command.Author, command.Type, command.ExpectedCount, command.Description);
        await db.SaveChangesAsync(ct);
    }
}
