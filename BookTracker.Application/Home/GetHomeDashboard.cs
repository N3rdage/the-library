using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Home;

// Read-model for the / dashboard. Relocated from HomeViewModel's inline
// DbContext reads in PR6b. Library totals + the top-10 authors and genres.
//
// Note on the author tally: this counts WorkAuthor rows (Role = Author) per
// canonical author — a co-authored work contributes to BOTH credited authors.
// That's deliberately a DIFFERENT metric from the /authors list's distinct
// book/work rollup (GetAuthorList) — kept separate so this stays a true
// "most contributions" headline.
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

        // Group authorship records by the canonical author id (or own id if
        // canonical), then look up the canonical name for display. Each
        // WorkAuthor row contributes one tally — co-authored works contribute
        // to BOTH credited authors. Default filter: Role = Author so
        // Editor/Translator/Illustrator don't pollute the headline.
        var authorTotals = await db.WorkAuthors
            .AsNoTracking()
            .Where(wa => wa.Role == AuthorRole.Author)
            .GroupBy(wa => wa.Author.CanonicalAuthorId ?? wa.AuthorId)
            .Select(g => new { CanonicalId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(ct);

        var canonicalIds = authorTotals.Select(t => t.CanonicalId).ToList();
        var nameLookup = await db.Authors
            .Where(a => canonicalIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var topAuthors = authorTotals
            .Select(t => new AuthorCount(t.CanonicalId, nameLookup.GetValueOrDefault(t.CanonicalId) ?? "(unknown)", t.Count))
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.Author)
            .ToList();

        var genres = await db.Genres
            .AsNoTracking()
            .Select(g => new { Genre = g.Name, Count = g.Works.Count })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Genre)
            .Take(10)
            .ToListAsync(ct);
        var topGenres = genres.Select(x => new GenreCount(x.Genre, x.Count)).ToList();

        return new HomeDashboard(totalBooks, totalAuthors, topAuthors, topGenres);
    }
}
