using BookTracker.Application.Duplicates;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

/// <summary>Which of these titles by this author does the library own? Each
/// title is a provider's (Open Library's) and is compared to the library's
/// Works by normalised title; the author is matched by normalised name on a
/// Work's lead author, with pen names rolled up both ways through
/// <c>CanonicalAuthorId</c>. Returns, keyed by the title string as given,
/// the lowest-Id live Book holding a matching Work; titles the library does
/// not own are absent. The matching lives here rather than in the endpoint
/// because <see cref="DuplicateNormalization"/> is internal to Application.</summary>
public sealed record GetOwnedWorksByAuthor(string Author, IReadOnlyList<string> Titles)
    : IQuery<IReadOnlyDictionary<string, int>>;

public sealed class GetOwnedWorksByAuthorHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetOwnedWorksByAuthor, IReadOnlyDictionary<string, int>>
{
    public async Task<IReadOnlyDictionary<string, int>> HandleAsync(
        GetOwnedWorksByAuthor query, CancellationToken ct = default)
    {
        var owned = new Dictionary<string, int>();
        var wanted = DuplicateNormalization.Author(query.Author);
        if (wanted.Length == 0 || query.Titles.Count == 0) return owned;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Contains on the last normalised token narrows loosely in SQL (the
        // default collation is case-insensitive, and the token survives
        // punctuation in the stored name, e.g. "brien" in "O'Brien") so the
        // exact normalised comparison can run in memory.
        var surname = wanted[(wanted.LastIndexOf(' ') + 1)..];
        var candidates = await db.Authors
            .AsNoTracking()
            .Where(a => a.Name.Contains(surname))
            .Select(a => new { a.Id, a.Name, a.CanonicalAuthorId })
            .ToListAsync(ct);

        // Roll up to the canonical (an alias's CanonicalAuthorId, else the
        // author itself), then take every author under those canonicals:
        // asking for King finds Bachman's works, asking for Bachman finds
        // King's, the same grouping as the CanonicalAuthorId ?? Id rollups.
        var roots = candidates
            .Where(a => DuplicateNormalization.Author(a.Name) == wanted)
            .Select(a => a.CanonicalAuthorId ?? a.Id)
            .Distinct()
            .ToList();
        if (roots.Count == 0) return owned;

        var authorIds = await db.Authors
            .AsNoTracking()
            .Where(a => roots.Contains(a.Id) || (a.CanonicalAuthorId != null && roots.Contains(a.CanonicalAuthorId.Value)))
            .Select(a => a.Id)
            .ToListAsync(ct);

        // Books is the skip navigation, so the soft-delete filter on Book
        // applies: a Work held only by tombstoned Books is not owned.
        var works = await db.Works
            .AsNoTracking()
            .Where(w => authorIds.Contains(w.WorkAuthors
                .Where(wa => wa.Role == AuthorRole.Author)
                .OrderBy(wa => wa.Order)
                .Select(wa => wa.AuthorId)
                .FirstOrDefault()))
            .Where(w => w.Books.Any())
            .Select(w => new
            {
                w.Title,
                FirstBookId = w.Books.OrderBy(b => b.Id).Select(b => b.Id).FirstOrDefault(),
            })
            .ToListAsync(ct);

        // Several Works can share a normalised title (two Books holding the
        // same story); the lowest BookId wins so the answer is stable.
        var byTitle = works
            .GroupBy(w => DuplicateNormalization.Title(w.Title))
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.Min(w => w.FirstBookId));

        foreach (var title in query.Titles)
        {
            if (byTitle.TryGetValue(DuplicateNormalization.Title(title), out var bookId))
                owned.TryAdd(title, bookId);
        }
        return owned;
    }
}
