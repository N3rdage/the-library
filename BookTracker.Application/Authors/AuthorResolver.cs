using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

// Find-or-create entry point used at every Work save site to map the
// user-entered author-name string to an Author entity. Drew picked
// auto-create (option A) when designing the pen-name PR — typing a
// fresh name silently creates a canonical Author. Dedup happens later
// via the /authors page (mark one as alias of another) or via the
// follow-up merge UI (TODO.md).
//
// Lives in the application layer (relocated from Web in the back-end
// refactor) so command handlers can resolve authors without reaching up
// into Web. Web ViewModels that still create Works directly (Add / Bulk Add)
// reference it here until they migrate. Find-or-create has the same
// check-then-insert race as PublisherResolver — see TECH-DEBT TD-15.
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

    // NOTE: the former AssignAuthors(work, authors, contributors) now lives on
    // the Work aggregate as Work.AssignAuthorship (the ≥1-contributor invariant
    // belongs on the aggregate). Resolve names here, then call work.AssignAuthorship.
}
