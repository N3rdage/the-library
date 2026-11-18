using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Genres;

// Shared genre read-models. TopGenresAsync serves GetHomeDashboard (the /
// dashboard's top-genres card) and GetAiLibraryProfile (the AI prompt's
// library profile): `Genres → Works.Count, drop empties, order by count`, one
// definition; both consumers map the tuples to their own display/prompt shape.
public static class GenreReads
{
    // Genres carrying at least one Work, most-used first (name as a stable
    // tiebreak so equal-count genres order deterministically).
    public static async Task<IReadOnlyList<(string Name, int Count)>> TopGenresAsync(
        BookTrackerDbContext db, int take, CancellationToken ct)
    {
        var rows = await db.Genres
            .AsNoTracking()
            .Select(g => new { g.Name, Count = g.Works.Count })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name)
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(x => (x.Name, x.Count)).ToList();
    }
}
