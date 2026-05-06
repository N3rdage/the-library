using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Backs /authors as a compact searchable list. Per-row counts (works,
// books, series) are rolled up onto canonical rows — Stephen King's
// counts include Bachman titles when Bachman is marked as an alias.
// Alias rows show their own counts only.
//
// Detail / rename / alias-management lives at /authors/{id} via
// AuthorDetailViewModel — this VM only renders the listing.
public class AuthorListViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public bool Loading { get; private set; } = true;
    public List<AuthorRow> Authors { get; private set; } = [];

    /// <summary>Free-text filter; matches the row name OR any of its alias names so that
    /// typing a pen name surfaces the canonical row even when the alias row is hidden.</summary>
    public string SearchTerm { get; set; } = "";

    /// <summary>When false, only canonical authors render. Defaults to true (show every row).</summary>
    public bool ShowAliases { get; set; } = true;

    /// <summary>Filter applied to <see cref="Authors"/> using <see cref="SearchTerm"/> and <see cref="ShowAliases"/>.</summary>
    public IEnumerable<AuthorRow> FilteredAuthors
    {
        get
        {
            IEnumerable<AuthorRow> q = Authors;

            if (!ShowAliases)
            {
                q = q.Where(a => a.CanonicalAuthorId is null);
            }

            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                var term = SearchTerm.Trim();
                q = q.Where(a =>
                    a.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    a.AliasNames.Any(n => n.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            return q;
        }
    }

    public async Task LoadAsync()
    {
        Loading = true;
        await using var db = await dbFactory.CreateDbContextAsync();

        // Pull author + alias structure once. Counts are computed in-app from
        // bulk-loaded join data — keeps the SQL simple (avoids the EF Core 10.x
        // record-projection pitfalls) and the post-processing trivial at the
        // 3000+ books target.
        var authorsRaw = await db.Authors
            .Include(a => a.CanonicalAuthor)
            .Include(a => a.Aliases)
            .OrderBy(a => a.Name)
            .ToListAsync();

        var workAuthors = await db.WorkAuthors
            .Select(wa => new { wa.AuthorId, wa.WorkId })
            .ToListAsync();

        var workSeries = await db.Works
            .Where(w => w.SeriesId != null)
            .Select(w => new { WorkId = w.Id, SeriesId = w.SeriesId!.Value })
            .ToListAsync();

        var bookWorkPairs = await db.Books
            .SelectMany(b => b.Works.Select(w => new { BookId = b.Id, WorkId = w.Id }))
            .ToListAsync();

        var worksByAuthor = workAuthors
            .GroupBy(wa => wa.AuthorId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.WorkId).ToHashSet());

        var seriesByWork = workSeries
            .GroupBy(w => w.WorkId)
            .ToDictionary(g => g.Key, g => g.First().SeriesId);

        var booksByWork = bookWorkPairs
            .GroupBy(x => x.WorkId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.BookId).ToHashSet());

        var rows = new List<AuthorRow>(authorsRaw.Count);
        foreach (var a in authorsRaw)
        {
            // Canonical rollup: own author id PLUS every alias id pointing at it.
            // Alias rows just count their own works/books/series — they exist
            // mainly so the user can promote / rename / remove the alias link.
            var rollupAuthorIds = a.CanonicalAuthorId is null
                ? new[] { a.Id }.Concat(a.Aliases.Select(al => al.Id)).ToHashSet()
                : new HashSet<int> { a.Id };

            var workIds = rollupAuthorIds
                .SelectMany(id => worksByAuthor.GetValueOrDefault(id, []))
                .ToHashSet();

            var bookIds = workIds
                .SelectMany(wId => booksByWork.GetValueOrDefault(wId, []))
                .ToHashSet();

            var seriesIds = workIds
                .Where(wId => seriesByWork.ContainsKey(wId))
                .Select(wId => seriesByWork[wId])
                .ToHashSet();

            rows.Add(new AuthorRow(
                a.Id,
                a.Name,
                a.CanonicalAuthorId,
                a.CanonicalAuthor?.Name,
                a.Aliases.Select(al => al.Name).OrderBy(n => n).ToList(),
                workIds.Count,
                bookIds.Count,
                seriesIds.Count));
        }

        Authors = rows;
        Loading = false;
    }

    public record AuthorRow(
        int Id,
        string Name,
        int? CanonicalAuthorId,
        string? CanonicalName,
        IReadOnlyList<string> AliasNames,
        int WorkCount,
        int BookCount,
        int SeriesCount);
}
