using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Ai;

// Read-model for the AI collection-suggestion prompt: every book that belongs
// to no series, by title, with its works' author names joined ("A, B"). The
// existing series the prompt lists alongside come from GetSeriesList, which
// the provider dispatches separately.
//
// Author names come through Work.Authors, the role-blind skip navigation, so
// an editor or translator credited on a work is listed as an author too.
public sealed record GetUnseriesedBooksForAi : IQuery<IReadOnlyList<UnseriesedBook>>;

public record UnseriesedBook(string Title, string Author);

public sealed class GetUnseriesedBooksForAiHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetUnseriesedBooksForAi, IReadOnlyList<UnseriesedBook>>
{
    public async Task<IReadOnlyList<UnseriesedBook>> HandleAsync(GetUnseriesedBooksForAi query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Books
            .AsNoTracking()
            .Where(b => b.SeriesId == null)
            .OrderBy(b => b.Title)
            .Select(b => new UnseriesedBook(
                b.Title,
                string.Join(", ", b.Works.SelectMany(w => w.Authors.Select(a => a.Name)).Distinct())))
            .ToListAsync(ct);
    }
}
