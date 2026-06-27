using BookTracker.Application.Authors;
using BookTracker.Application.Genres;
using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Home;

// Read-model for the / dashboard. Library totals + the top-10 authors and genres.
//
// Top authors rank canonical authors by distinct book count (pen-name aliases
// roll up under their canonical — Bachman titles count toward King) via the
// shared AuthorRollups, so /authors, the mobile snapshot, and this card all read
// one definition. Top genres come from the shared GenreReads.
public sealed record GetHomeDashboard : IQuery<HomeDashboard>;

public record HomeDashboard(
    int TotalBooks,
    int TotalAuthors,
    IReadOnlyList<AuthorCount> TopAuthors,
    IReadOnlyList<GenreCount> TopGenres);

public record AuthorCount(int CanonicalAuthorId, string Author, int Count);
public record GenreCount(string Genre, int Count);

public sealed class GetHomeDashboardHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetHomeDashboard, HomeDashboard>
{
    public async Task<HomeDashboard> HandleAsync(GetHomeDashboard query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var totalBooks = await db.Books.CountAsync(ct);

        // "Authors" counts canonical Author entities — pen-name aliases roll
        // up under their canonical (e.g. Bachman titles count toward King).
        var totalAuthors = await db.Authors
            .Where(a => a.CanonicalAuthorId == null)
            .CountAsync(ct);

        // Top canonical authors by distinct book count. Roll per-author counts up
        // onto canonicals (aliases fold into their canonical), drop authors with
        // no surviving books so a works-but-all-tombstoned author can't land a
        // "0 books" row in the headline, order by count then id for a
        // deterministic boundary, then sort for display (count desc, name asc).
        var perAuthor = await AuthorRollups.PerAuthorAsync(db, ct);
        var membership = await db.Authors
            .AsNoTracking()
            .Select(a => new { a.Id, Canonical = a.CanonicalAuthorId ?? a.Id })
            .ToListAsync(ct);
        var rollup = AuthorRollups.RollUpToCanonical(
            perAuthor, membership.Select(m => (m.Id, m.Canonical)));
        var top = rollup
            .Where(kv => kv.Value.BookCount > 0)
            .OrderByDescending(kv => kv.Value.BookCount)
            .ThenBy(kv => kv.Key)
            .Take(10)
            .ToList();

        var canonicalIds = top.Select(t => t.Key).ToList();
        var nameLookup = await db.Authors
            .Where(a => canonicalIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var topAuthors = top
            .Select(t => new AuthorCount(t.Key, nameLookup.GetValueOrDefault(t.Key) ?? "(unknown)", t.Value.BookCount))
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.Author)
            .ToList();

        var topGenres = (await GenreReads.TopGenresAsync(db, 10, ct))
            .Select(g => new GenreCount(g.Name, g.Count))
            .ToList();

        return new HomeDashboard(totalBooks, totalAuthors, topAuthors, topGenres);
    }
}
