using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

// Read-model for the /series list page. Relocated from SeriesListViewModel's
// inline DbContext read in PR6b. Optional name/author substring + type filters;
// ordered by name. BookCount is the series' Books.Count nav aggregate (series
// membership is a per-Book concept — the Book is installment N).
public sealed record GetSeriesList(string? Search, SeriesType? Type)
    : IQuery<IReadOnlyList<SeriesListItem>>;

public record SeriesListItem(
    int Id, string Name, string? Author, SeriesType Type,
    int BookCount, int? ExpectedCount);

public sealed class GetSeriesListHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetSeriesList, IReadOnlyList<SeriesListItem>>
{
    public async Task<IReadOnlyList<SeriesListItem>> HandleAsync(GetSeriesList query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        IQueryable<Data.Models.Series> q = db.Series.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s => s.Name.Contains(term) || (s.Author != null && s.Author.Contains(term)));
        }

        if (query.Type is SeriesType type)
        {
            q = q.Where(s => s.Type == type);
        }

        return await q
            .OrderBy(s => s.Name)
            .Select(s => new SeriesListItem(
                s.Id,
                s.Name,
                s.Author,
                s.Type,
                s.Books.Count,
                s.ExpectedCount))
            .ToListAsync(ct);
    }
}
