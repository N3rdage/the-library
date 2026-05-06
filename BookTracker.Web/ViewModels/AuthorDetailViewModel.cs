using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Backs /authors/{id}. Owns rename + alias-management ops and the
// per-author drill-down (Works / Books) that previously lived inside
// AuthorListViewModel's inline expand. Each detail VM is bound to a
// single author id loaded via LoadAsync(authorId).
public class AuthorDetailViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public bool Loading { get; private set; } = true;
    public bool NotFound { get; private set; }
    public AuthorHeader? Header { get; private set; }
    public AuthorDetail Detail { get; private set; } = AuthorDetail.Empty;
    public AuthorViewMode ViewMode { get; set; } = AuthorViewMode.Works;

    /// <summary>List of canonical authors used by the "mark as alias of…" dropdown.</summary>
    public List<CanonicalCandidate> CanonicalCandidates { get; private set; } = [];

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task LoadAsync(int authorId)
    {
        Loading = true;
        NotFound = false;
        Header = null;
        Detail = AuthorDetail.Empty;

        await using var db = await dbFactory.CreateDbContextAsync();

        var author = await db.Authors
            .Include(a => a.CanonicalAuthor)
            .Include(a => a.Aliases)
            .FirstOrDefaultAsync(a => a.Id == authorId);

        if (author is null)
        {
            NotFound = true;
            Loading = false;
            return;
        }

        Header = new AuthorHeader(
            author.Id,
            author.Name,
            author.CanonicalAuthorId,
            author.CanonicalAuthor?.Name);

        Detail = await LoadDetailAsync(db, author);

        // Canonical-candidate list for the "mark as alias of" dropdown — every
        // canonical author except this one. Avoid alias-of-alias chains (the
        // existing rule on save also re-roots, but this trims the dropdown).
        CanonicalCandidates = await db.Authors
            .Where(a => a.CanonicalAuthorId == null && a.Id != authorId)
            .OrderBy(a => a.Name)
            .Select(a => new CanonicalCandidate(a.Id, a.Name))
            .ToListAsync();

        Loading = false;
    }

    private async Task<AuthorDetail> LoadDetailAsync(BookTrackerDbContext db, Author author)
    {
        bool isCanonical = author.CanonicalAuthorId is null;

        var ids = new List<int> { author.Id };
        if (isCanonical)
        {
            ids.AddRange(author.Aliases.Select(a => a.Id));
        }

        var works = await db.Works
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(w => w.Books)
            .Include(w => w.Series)
            .Where(w => w.WorkAuthors.Any(wa => ids.Contains(wa.AuthorId)))
            .OrderBy(w => w.SeriesId == null)
            .ThenBy(w => w.Series!.Name)
            .ThenBy(w => w.SeriesOrder ?? int.MaxValue)
            .ThenBy(w => w.Title)
            .ToListAsync();

        var workRows = works.Select(w => new WorkRow(
            w.Id,
            w.Title,
            w.Subtitle,
            // "Written as" tag only on canonical drill-downs; alias rows always
            // are themselves so the label would be redundant.
            isCanonical
                ? w.WorkAuthors.OrderBy(wa => wa.Order).Select(wa => wa.Author).FirstOrDefault() is { } leadAuthor && leadAuthor.Id != author.Id
                    ? leadAuthor.Name
                    : null
                : null,
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

    public async Task RenameAsync(string newName)
    {
        if (Header is null) return;
        var trimmed = newName.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var author = await db.Authors.FirstOrDefaultAsync(a => a.Id == Header.Id);
        if (author is null) return;

        var clash = await db.Authors.AnyAsync(a => a.Id != Header.Id && a.Name == trimmed);
        if (clash)
        {
            ErrorMessage = $"An author named \"{trimmed}\" already exists. Use the alias dropdown to merge.";
            return;
        }

        author.Name = trimmed;
        await db.SaveChangesAsync();
        SuccessMessage = $"Renamed to \"{trimmed}\".";
        await LoadAsync(Header.Id);
    }

    public async Task MarkAsAliasOfAsync(int canonicalId)
    {
        if (Header is null || Header.Id == canonicalId) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var alias = await db.Authors.FirstOrDefaultAsync(a => a.Id == Header.Id);
        var canonical = await db.Authors.FirstOrDefaultAsync(a => a.Id == canonicalId);
        if (alias is null || canonical is null) return;

        // Re-root if the chosen "canonical" is itself an alias — avoids alias-of-alias chains.
        var rootCanonicalId = canonical.CanonicalAuthorId ?? canonical.Id;

        alias.CanonicalAuthorId = rootCanonicalId;

        // Re-point any prior aliases that targeted this row at the new root.
        var prior = await db.Authors.Where(a => a.CanonicalAuthorId == Header.Id).ToListAsync();
        foreach (var p in prior)
        {
            p.CanonicalAuthorId = rootCanonicalId;
        }

        await db.SaveChangesAsync();
        SuccessMessage = $"\"{alias.Name}\" is now an alias of \"{canonical.Name}\".";
        await LoadAsync(Header.Id);
    }

    public async Task PromoteToCanonicalAsync()
    {
        if (Header is null || Header.CanonicalAuthorId is null) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var alias = await db.Authors.FirstOrDefaultAsync(a => a.Id == Header.Id);
        if (alias is null || alias.CanonicalAuthorId is null) return;

        alias.CanonicalAuthorId = null;
        await db.SaveChangesAsync();
        SuccessMessage = $"\"{alias.Name}\" is now its own canonical author.";
        await LoadAsync(Header.Id);
    }

    public record AuthorHeader(int Id, string Name, int? CanonicalAuthorId, string? CanonicalName);

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

    public record CanonicalCandidate(int Id, string Name);

    public enum AuthorViewMode { Works, Books }
}
