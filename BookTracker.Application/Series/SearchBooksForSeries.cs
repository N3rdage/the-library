using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

// Read-model for the Series edit page's "add book" search: up to 10 books, by
// title, whose title, a Work title or a credited name (any role, via the
// Work.Authors skip navigation) contains Term, excluding ExcludeIds (the books
// already in the table). A blank term returns nothing. Relocated from
// SeriesEditViewModel's tracked Include search (#265).
public sealed record SearchBooksForSeries(string Term, IReadOnlyCollection<int> ExcludeIds)
    : IQuery<IReadOnlyList<SeriesBookSearchResult>>;

public record SeriesBookSearchResult(int Id, string Title, string Author, int? CurrentSeriesId);

public sealed class SearchBooksForSeriesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<SearchBooksForSeries, IReadOnlyList<SeriesBookSearchResult>>
{
    public async Task<IReadOnlyList<SeriesBookSearchResult>> HandleAsync(SearchBooksForSeries query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Term)) return [];

        var term = query.Term.Trim();
        var excludeIds = query.ExcludeIds.ToList();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var matches = await db.Books
            .AsNoTracking()
            .Where(b => !excludeIds.Contains(b.Id))
            .Where(b => b.Title.Contains(term) || b.Works.Any(w => w.Title.Contains(term) || w.Authors.Any(a => a.Name.Contains(term))))
            .OrderBy(b => b.Title)
            .Take(10)
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.SeriesId,
                AuthorsPerWork = b.Works
                    .Select(w => w.WorkAuthors
                        .Where(wa => wa.Role == AuthorRole.Author)
                        .OrderBy(wa => wa.Order)
                        .Select(wa => wa.Author.Name)
                        .ToList())
                    .ToList(),
            })
            .ToListAsync(ct);

        return matches
            .Select(b => new SeriesBookSearchResult(b.Id, b.Title, SeriesBookAuthors.Display(b.AuthorsPerWork), b.SeriesId))
            .ToList();
    }
}
