using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

// Find-or-create entry point used at every Work save site to map the
// user-entered author-name string to an Author entity. Drew picked
// auto-create (option A) when designing the pen-name PR — typing a
// fresh name silently creates a canonical Author. Dedup happens later
// via the /authors page (mark one as alias of another) or via the
// follow-up merge UI (TODO.md).
//
// Multi-author cutover (PR1 of #14): the batch helpers below take a list
// of names and produce ordered Author entities + WorkAuthor join rows.
// Save sites still set Work.Author to the lead (legacy compat) AND
// populate Work.WorkAuthors with all authors; PR2 will drop Work.AuthorId
// and switch reads to the join.
public static class AuthorResolver
{
    public static async Task<Author> FindOrCreateAsync(string name, BookTrackerDbContext db, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Author name must be non-empty.", nameof(name));
        }

        var existing = await db.Authors.FirstOrDefaultAsync(a => a.Name == trimmed, ct);
        if (existing is not null) return existing;

        var fresh = new Author { Name = trimmed };
        db.Authors.Add(fresh);
        return fresh;
    }

    /// <summary>
    /// Sequentially find-or-create each name; preserves input order, drops
    /// blanks, de-duplicates by trimmed name (case-insensitive). Returned
    /// list maps positionally to the authors as they should be displayed.
    /// </summary>
    public static async Task<List<Author>> FindOrCreateAllAsync(IEnumerable<string> names, BookTrackerDbContext db, CancellationToken ct = default)
    {
        var result = new List<Author>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in names)
        {
            var trimmed = raw?.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (!seen.Add(trimmed)) continue;
            result.Add(await FindOrCreateAsync(trimmed, db, ct));
        }
        return result;
    }

    /// <summary>
    /// Parse a free-form author string into a list of names. Splits on commas
    /// and ampersands so "Preston, Lincoln Child" and "Preston &amp; Child"
    /// both become two names. Used by surfaces (Bulk Add) where the user
    /// types into a single cell rather than a chip picker.
    /// </summary>
    public static List<string> ParseNames(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw
            .Split(['&', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    /// <summary>
    /// Replace Work.WorkAuthors with one join row per author, Order ascending
    /// from 0. Caller is responsible for ensuring authors come from
    /// FindOrCreateAllAsync (so brand-new ones are already attached to the
    /// DbContext) and for SaveChangesAsync.
    /// </summary>
    public static void AssignAuthors(Work work, IReadOnlyList<Author> authors)
    {
        if (authors.Count == 0)
        {
            throw new ArgumentException("At least one author required.", nameof(authors));
        }

        work.WorkAuthors.Clear();
        for (var i = 0; i < authors.Count; i++)
        {
            work.WorkAuthors.Add(new WorkAuthor
            {
                Author = authors[i],
                Order = i,
            });
        }
    }
}
