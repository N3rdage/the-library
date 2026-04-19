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
}
