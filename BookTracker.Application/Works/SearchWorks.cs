using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

// Read-model for the existing-Work typeahead (Add page collection rows and the
// Book Detail "Add Work" dialog): Works whose title contains the query, minus
// those already attached to ExcludeBookId, ranked starts-with first then
// alphabetical, capped at Max. A query shorter than two characters (after
// trimming) returns nothing. Relocated from Web's WorkSearchService (#265).
public sealed record SearchWorks(string Query, int? ExcludeBookId, int Max)
    : IQuery<IReadOnlyList<WorkSearchResult>>;

public record WorkSearchResult(
    int Id,
    string Title,
    string? Subtitle,
    string AuthorName,
    /// <summary>Year only — helps disambiguate same-titled Works ("The Call
    /// of Cthulhu" by Lovecraft 1928 vs an unrelated novel). Null when the
    /// Work has no recorded date.</summary>
    int? FirstPublishedYear,
    int BookCount);

public sealed class SearchWorksHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<SearchWorks, IReadOnlyList<WorkSearchResult>>
{
    public async Task<IReadOnlyList<WorkSearchResult>> HandleAsync(SearchWorks query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Query) || query.Query.Trim().Length < 2)
        {
            return [];
        }

        var q = query.Query.Trim().ToLower();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Exclude Works already attached to the given Book so the caller
        // doesn't get "attach this Work that's already here" options in
        // the dropdown.
        var worksQuery = db.Works.AsNoTracking();
        if (query.ExcludeBookId is int excludeId)
        {
            worksQuery = worksQuery.Where(w => !w.Books.Any(b => b.Id == excludeId));
        }

        // Server-evaluable substring + case filter (SQL Server's default
        // collation is case-insensitive already; ToLower keeps the match
        // explicit).
        var matches = await worksQuery
            .Where(w => w.Title.ToLower().Contains(q))
            .Select(w => new
            {
                w.Id,
                w.Title,
                w.Subtitle,
                // Lead-author name only — the search dropdown row is tight, so
                // co-authors are elided here. The full multi-author display
                // appears once the user picks the work and lands on BookDetail.
                AuthorName = w.WorkAuthors.Where(wa => wa.Role == AuthorRole.Author).OrderBy(wa => wa.Order).Select(wa => wa.Author.Name).FirstOrDefault() ?? "",
                Year = (int?)(w.FirstPublishedDate == null ? null : (int?)w.FirstPublishedDate.Value.Year),
                BookCount = w.Books.Count,
                TitleLower = w.Title.ToLower()
            })
            .Take(query.Max * 3)
            .ToListAsync(ct);

        // Rank in memory: starts-with first, then contains anywhere,
        // alphabetical within each group.
        return matches
            .OrderByDescending(m => m.TitleLower.StartsWith(q))
            .ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
            .Take(query.Max)
            .Select(m => new WorkSearchResult(m.Id, m.Title, m.Subtitle, m.AuthorName, m.Year, m.BookCount))
            .ToList();
    }
}
