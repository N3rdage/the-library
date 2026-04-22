using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Backs the /authors page. Lists all Author entities with their work
// counts and supports the basic alias operations:
// - Mark a canonical Author as an alias of another canonical (Bachman → King)
// - Promote an alias back to canonical
// - Rename
// Author merging (combining two canonicals) is tracked in TODO.md.
//
// Each row can be expanded to show a drill-down with the author's Works
// or Books (per-row toggle). For canonical authors the drill-down rolls
// up any aliases — so Stephen King's expanded view includes Bachman
// titles, and a note surfaces which aliases contributed. Works/books
// load lazily on first expand; view-mode and expand state are kept on
// the VM so flipping back and forth doesn't re-hit the DB.
public class AuthorListViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public bool Loading { get; private set; } = true;
    public List<AuthorRow> Authors { get; private set; } = [];
    public string? SuccessMessage { get; set; }

    public HashSet<int> ExpandedAuthorIds { get; private set; } = [];
    public Dictionary<int, AuthorDetail> DetailByAuthorId { get; private set; } = [];
    public Dictionary<int, AuthorViewMode> ViewModeByAuthorId { get; private set; } = [];

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

    public async Task ToggleExpandAsync(int authorId)
    {
        if (ExpandedAuthorIds.Remove(authorId)) return;
        ExpandedAuthorIds.Add(authorId);
        if (!DetailByAuthorId.ContainsKey(authorId))
        {
            DetailByAuthorId[authorId] = await LoadDetailAsync(authorId);
        }
    }

    /// <summary>Pre-expand (idempotent) — used when /authors?expand=<id> is followed.</summary>
    public async Task ExpandAsync(int authorId)
    {
        ExpandedAuthorIds.Add(authorId);
        if (!DetailByAuthorId.ContainsKey(authorId))
        {
            DetailByAuthorId[authorId] = await LoadDetailAsync(authorId);
        }
    }

    public AuthorViewMode GetViewMode(int authorId) =>
        ViewModeByAuthorId.TryGetValue(authorId, out var m) ? m : AuthorViewMode.Works;

    public void SetViewMode(int authorId, AuthorViewMode mode) =>
        ViewModeByAuthorId[authorId] = mode;

    private async Task<AuthorDetail> LoadDetailAsync(int authorId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var author = await db.Authors
            .Include(a => a.Aliases)
            .FirstOrDefaultAsync(a => a.Id == authorId);
        if (author is null) return AuthorDetail.Empty;

        bool isCanonical = author.CanonicalAuthorId is null;

        // Collect every author id that rolls up into this view — the
        // author itself plus (for canonicals) every alias that points at it.
        var ids = new List<int> { authorId };
        if (isCanonical)
        {
            ids.AddRange(author.Aliases.Select(a => a.Id));
        }

        var works = await db.Works
            .Include(w => w.Author)
            .Include(w => w.Books)
            .Include(w => w.Series)
            .Where(w => ids.Contains(w.AuthorId))
            .OrderBy(w => w.Title)
            .ToListAsync();

        var workRows = works.Select(w => new WorkRow(
            w.Id,
            w.Title,
            w.Subtitle,
            // Flag which alias a work was written as, but only on canonical
            // rows (on an alias row the author is always itself). For single-
            // identity canonicals this is always null.
            isCanonical && w.AuthorId != authorId ? w.Author.Name : null,
            PartialDateParser.Format(w.FirstPublishedDate, w.FirstPublishedDatePrecision),
            w.Series?.Name,
            w.Series?.Type,
            w.SeriesOrder,
            w.Books.Select(b => new BookRef(b.Id, b.Title)).ToList()
        )).ToList();

        var bookIds = works.SelectMany(w => w.Books).Select(b => b.Id).Distinct().ToList();
        var books = await db.Books
            .Where(b => bookIds.Contains(b.Id))
            .Include(b => b.Editions).ThenInclude(e => e.Copies)
            .OrderBy(b => b.Title)
            .ToListAsync();

        var bookRows = books.Select(b => new BookSummaryRow(
            b.Id,
            b.Title,
            b.DefaultCoverArtUrl,
            b.Editions.Count,
            b.Editions.Sum(e => e.Copies.Count)
        )).ToList();

        var aliasNames = isCanonical
            ? author.Aliases.Select(a => a.Name).OrderBy(n => n).ToList()
            : new List<string>();

        return new AuthorDetail(workRows, bookRows, aliasNames);
    }

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
        InvalidateDetailsFor(aliasId, canonicalId, rootCanonicalId);
        await LoadAsync();
    }

    public async Task PromoteToCanonicalAsync(int aliasId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var alias = await db.Authors.FirstOrDefaultAsync(a => a.Id == aliasId);
        if (alias is null || alias.CanonicalAuthorId is null) return;

        var priorCanonicalId = alias.CanonicalAuthorId.Value;
        alias.CanonicalAuthorId = null;
        await db.SaveChangesAsync();
        SuccessMessage = $"\"{alias.Name}\" is now its own canonical author.";
        InvalidateDetailsFor(aliasId, priorCanonicalId);
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
        InvalidateDetailsFor(authorId);
        await LoadAsync();
    }

    // Any structural change (alias / promote / rename) potentially invalidates
    // the cached drill-down detail for the affected authors — drop them so
    // the next expand reloads fresh from the DB.
    private void InvalidateDetailsFor(params int[] authorIds)
    {
        foreach (var id in authorIds) DetailByAuthorId.Remove(id);
    }

    public record AuthorRow(int Id, string Name, int? CanonicalAuthorId, string? CanonicalName, int WorkCount);

    public record AuthorDetail(
        IReadOnlyList<WorkRow> Works,
        IReadOnlyList<BookSummaryRow> Books,
        IReadOnlyList<string> AliasNames)
    {
        public static AuthorDetail Empty => new([], [], []);
    }

    public record WorkRow(
        int Id,
        string Title,
        string? Subtitle,
        string? WrittenAs,
        string FirstPublishedDisplay,
        string? SeriesName,
        SeriesType? SeriesType,
        int? SeriesOrder,
        IReadOnlyList<BookRef> InBooks);

    public record BookSummaryRow(
        int Id,
        string Title,
        string? CoverUrl,
        int EditionCount,
        int CopyCount);

    public record BookRef(int Id, string Title);

    public enum AuthorViewMode { Works, Books }
}
