using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

// Read-model for the /authors list. Per-row counts (works, books, series) roll
// up onto canonical rows — Stephen King's counts include Bachman titles when
// Bachman is an alias; alias rows show their own counts only.
//
// Counts come from the shared SQL-side AuthorRollups (close-out consolidation):
// canonical rows read the canonical rollup (own + aliases), alias rows read their
// own. The list page does its own free-text / show-aliases filtering in-memory
// over these rows.
public sealed record GetAuthorList : IQuery<IReadOnlyList<AuthorRow>>;

public record AuthorRow(
    int Id,
    string Name,
    int? CanonicalAuthorId,
    string? CanonicalName,
    IReadOnlyList<string> AliasNames,
    int WorkCount,
    int BookCount,
    int SeriesCount);

public sealed class GetAuthorListHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetAuthorList, IReadOnlyList<AuthorRow>>
{
    public async Task<IReadOnlyList<AuthorRow>> HandleAsync(GetAuthorList query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var authorsRaw = await db.Authors
            .AsNoTracking()
            .Include(a => a.CanonicalAuthor)
            .Include(a => a.Aliases)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        // SQL-side per-author distinct counts (Author-role only). Canonical rows
        // read the rolled-up totals (own + aliases, summed); alias rows read their
        // own. An author with no Author-role works has no entry in either map →
        // 0s via the null-coalesce below.
        var perAuthor = await AuthorRollups.PerAuthorAsync(db, ct);
        var byCanonical = AuthorRollups.RollUpToCanonical(
            perAuthor, authorsRaw.Select(a => (a.Id, a.CanonicalAuthorId ?? a.Id)));

        var rows = new List<AuthorRow>(authorsRaw.Count);
        foreach (var a in authorsRaw)
        {
            var counts = a.CanonicalAuthorId is null
                ? byCanonical.GetValueOrDefault(a.Id)
                : perAuthor.GetValueOrDefault(a.Id);

            rows.Add(new AuthorRow(
                a.Id,
                a.Name,
                a.CanonicalAuthorId,
                a.CanonicalAuthor?.Name,
                a.Aliases.Select(al => al.Name).OrderBy(n => n).ToList(),
                counts?.WorkCount ?? 0,
                counts?.BookCount ?? 0,
                counts?.SeriesCount ?? 0));
        }

        return rows;
    }
}
