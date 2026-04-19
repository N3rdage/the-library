using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Backs the /authors page. Lists all Author entities with their work
// counts and supports the basic alias operations:
// - Mark a canonical Author as an alias of another canonical (Bachman → King)
// - Promote an alias back to canonical
// - Rename
// Author merging (combining two canonicals) is tracked in TODO.md.
public class AuthorListViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public bool Loading { get; private set; } = true;
    public List<AuthorRow> Authors { get; private set; } = [];
    public string? SuccessMessage { get; set; }

    public async Task LoadAsync()
    {
        Loading = true;
        await using var db = await dbFactory.CreateDbContextAsync();

        var raw = await db.Authors
            .Include(a => a.CanonicalAuthor)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.CanonicalAuthorId,
                CanonicalName = a.CanonicalAuthor != null ? a.CanonicalAuthor.Name : null,
                WorkCount = a.Works.Count
            })
            .OrderBy(a => a.Name)
            .ToListAsync();

        Authors = raw
            .Select(a => new AuthorRow(a.Id, a.Name, a.CanonicalAuthorId, a.CanonicalName, a.WorkCount))
            .ToList();

        Loading = false;
    }

    public IEnumerable<AuthorRow> CanonicalAuthors => Authors.Where(a => a.CanonicalAuthorId is null);

    public async Task MarkAsAliasAsync(int aliasId, int canonicalId)
    {
        if (aliasId == canonicalId) return; // can't alias to self

        await using var db = await dbFactory.CreateDbContextAsync();
        var alias = await db.Authors.FirstOrDefaultAsync(a => a.Id == aliasId);
        var canonical = await db.Authors.FirstOrDefaultAsync(a => a.Id == canonicalId);
        if (alias is null || canonical is null) return;

        // Don't chain aliases — point at the *root* canonical so two-hop
        // alias graphs don't form.
        var rootCanonicalId = canonical.CanonicalAuthorId ?? canonical.Id;

        alias.CanonicalAuthorId = rootCanonicalId;

        // Any author that previously aliased to this one should now alias to
        // the new root, otherwise we'd leave dangling chained aliases.
        var prior = await db.Authors.Where(a => a.CanonicalAuthorId == aliasId).ToListAsync();
        foreach (var p in prior)
        {
            p.CanonicalAuthorId = rootCanonicalId;
        }

        await db.SaveChangesAsync();
        SuccessMessage = $"\"{alias.Name}\" is now an alias of \"{canonical.Name}\".";
        await LoadAsync();
    }

    public async Task PromoteToCanonicalAsync(int aliasId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var alias = await db.Authors.FirstOrDefaultAsync(a => a.Id == aliasId);
        if (alias is null || alias.CanonicalAuthorId is null) return;

        alias.CanonicalAuthorId = null;
        await db.SaveChangesAsync();
        SuccessMessage = $"\"{alias.Name}\" is now its own canonical author.";
        await LoadAsync();
    }

    public async Task RenameAsync(int authorId, string newName)
    {
        var trimmed = newName.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var author = await db.Authors.FirstOrDefaultAsync(a => a.Id == authorId);
        if (author is null) return;

        // Prevent rename collision with another existing Author.
        var clash = await db.Authors.AnyAsync(a => a.Id != authorId && a.Name == trimmed);
        if (clash)
        {
            SuccessMessage = $"An author named \"{trimmed}\" already exists. Use the alias dropdown to merge.";
            return;
        }

        author.Name = trimmed;
        await db.SaveChangesAsync();
        SuccessMessage = $"Renamed to \"{trimmed}\".";
        await LoadAsync();
    }

    public record AuthorRow(int Id, string Name, int? CanonicalAuthorId, string? CanonicalName, int WorkCount);
}
