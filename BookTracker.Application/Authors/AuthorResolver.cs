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

    /// <summary>
    /// Replace Work.WorkAuthors with one Author-role join row per author
    /// (Order ascending from 0), optionally followed by non-Author contributor
    /// rows. Each non-Author role gets its own Order sequence (per-role) so
    /// "first translator" and "first illustrator" both sit at Order 0 within
    /// their role bucket. Caller is responsible for ensuring authors and
    /// contributor people come from FindOrCreate* (so brand-new ones are
    /// already attached to the DbContext) and for SaveChangesAsync.
    /// Editor-only Works (e.g. dictionaries) are valid — at least one
    /// contributor of any role must be supplied across the two lists.
    /// </summary>
    public static void AssignAuthors(
        Work work,
        IReadOnlyList<Author> authors,
        IReadOnlyList<(Author Person, AuthorRole Role)>? additionalContributors = null)
    {
        var hasNonAuthor = additionalContributors is { Count: > 0 }
            && additionalContributors.Any(c => c.Role != AuthorRole.Author);
        if (authors.Count == 0 && !hasNonAuthor)
        {
            throw new ArgumentException("At least one contributor required (author, editor, or other role).", nameof(authors));
        }

        work.WorkAuthors.Clear();
        for (var i = 0; i < authors.Count; i++)
        {
            work.WorkAuthors.Add(new WorkAuthor
            {
                Author = authors[i],
                Order = i,
                Role = AuthorRole.Author,
            });
        }

        if (additionalContributors is null || additionalContributors.Count == 0) return;

        // Per-role Order sequencing and (Author, Role) dedup. Reference-equality
        // dedup on Author works because callers route every name through
        // FindOrCreate* against the same DbContext — identical names produce
        // identical Author instances.
        var orderByRole = new Dictionary<AuthorRole, int>();
        var seen = new HashSet<(Author Person, AuthorRole Role)>();
        foreach (var (person, role) in additionalContributors)
        {
            if (role == AuthorRole.Author) continue;       // belongs in the main list
            if (!seen.Add((person, role))) continue;       // duplicate (Author, Role) pair
            var next = orderByRole.GetValueOrDefault(role, 0);
            work.WorkAuthors.Add(new WorkAuthor
            {
                Author = person,
                Order = next,
                Role = role,
            });
            orderByRole[role] = next + 1;
        }
    }
}
