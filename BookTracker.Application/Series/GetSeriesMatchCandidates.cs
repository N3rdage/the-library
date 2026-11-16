using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

// Read-model behind the Add / Bulk Add series suggestion. Each part runs only
// when its input is given:
// - SeriesName: the local Series whose name matches, case-insensitively;
// - Author: every Series whose display Author equals it, and, only when there
//   is none, the count of Books with no series that have a Work crediting that
//   author name (any role, via the Work.Authors skip navigation).
// Names are trimmed before matching. Relocated from Web's SeriesMatchService (#265);
// the suggestion rules stay in that service.
public sealed record GetSeriesMatchCandidates(string? SeriesName, string? Author)
    : IQuery<SeriesMatchCandidates>;

public record SeriesMatchCandidates(
    SeriesCandidate? NameMatch,
    IReadOnlyList<SeriesCandidate> AuthorSeries,
    int UnseriesedAuthorBookCount);

public record SeriesCandidate(int Id, string Name, SeriesType Type);

public sealed class GetSeriesMatchCandidatesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetSeriesMatchCandidates, SeriesMatchCandidates>
{
    public async Task<SeriesMatchCandidates> HandleAsync(GetSeriesMatchCandidates query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        SeriesCandidate? nameMatch = null;
        if (!string.IsNullOrWhiteSpace(query.SeriesName))
        {
            var name = query.SeriesName.Trim();
            nameMatch = await db.Series
                .AsNoTracking()
                .Where(s => s.Name.ToLower() == name.ToLower())
                .Select(s => new SeriesCandidate(s.Id, s.Name, s.Type))
                .FirstOrDefaultAsync(ct);
        }

        IReadOnlyList<SeriesCandidate> authorSeries = [];
        var unseriesedCount = 0;
        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            var authorName = query.Author.Trim();
            authorSeries = await db.Series
                .AsNoTracking()
                .Where(s => s.Author != null && s.Author == authorName)
                .Select(s => new SeriesCandidate(s.Id, s.Name, s.Type))
                .ToListAsync(ct);

            if (authorSeries.Count == 0)
            {
                // Series membership lives on the Book, so count this author's
                // books with no Book-level series.
                unseriesedCount = await db.Books
                    .AsNoTracking()
                    .CountAsync(b => b.SeriesId == null
                        && b.Works.Any(w => w.Authors.Any(a => a.Name == authorName)), ct);
            }
        }

        return new SeriesMatchCandidates(nameMatch, authorSeries, unseriesedCount);
    }
}
