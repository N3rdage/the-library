using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

// Read-model for the series dropdown on the Edit book details dialog: every
// series by name, with its type for the "(Series)" / "(Collection)" suffix.
// Relocated from BookEditDialogViewModel's inline DbContext read (#264).
public sealed record GetSeriesOptions : IQuery<IReadOnlyList<SeriesPickerOption>>;

public record SeriesPickerOption(int Id, string Name, SeriesType Type);

public sealed class GetSeriesOptionsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetSeriesOptions, IReadOnlyList<SeriesPickerOption>>
{
    public async Task<IReadOnlyList<SeriesPickerOption>> HandleAsync(GetSeriesOptions query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Series
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SeriesPickerOption(s.Id, s.Name, s.Type))
            .ToListAsync(ct);
    }
}
