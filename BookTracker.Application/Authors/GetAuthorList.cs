using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

// Read-model for the /authors list. Relocated from AuthorListViewModel's inline
// reads in PR6b. Per-row counts (works, books, series) roll up onto canonical
// rows — Stephen King's counts include Bachman titles when Bachman is an alias;
// alias rows show their own counts only.
//
// Counts are computed in-app from bulk-loaded join data — keeps the SQL simple
// (avoids the EF Core 10.x record-projection pitfalls) and the post-processing
// trivial at the 3000+ books target. The list page does its own free-text /
// show-aliases filtering in-memory over these rows.
public sealed record GetAuthorList : IQuery<IReadOnlyList<AuthorRow>>;

public record AuthorRow(
    int Id,
    string Name,
    int? CanonicalAuthorId,
    string? CanonicalName,
    IReadOnlyList<string> AliasNames,
    int WorkCount,
    int BookCount,
    int SeriesCount);

public sealed class GetAuthorListHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetAuthorList, IReadOnlyList<AuthorRow>>
{
    public async Task<IReadOnlyList<AuthorRow>> HandleAsync(GetAuthorList query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var authorsRaw = await db.Authors
            .AsNoTracking()
            .Include(a => a.CanonicalAuthor)
            .Include(a => a.Aliases)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        // Default rollup: Role = Author only. Editor/Translator/Illustrator
        // contributions are intentionally excluded from the headline "books
        // by X" count so reference-work translators don't pollute the list.
        var workAuthors = await db.WorkAuthors
            .AsNoTracking()
            .Where(wa => wa.Role == AuthorRole.Author)
            .Select(wa => new { wa.AuthorId, wa.WorkId })
            .ToListAsync(ct);

        var workSeries = await db.Works
            .AsNoTracking()
            .Where(w => w.SeriesId != null)
            .Select(w => new { WorkId = w.Id, SeriesId = w.SeriesId!.Value })
            .ToListAsync(ct);

        var bookWorkPairs = await db.Books
            .AsNoTracking()
            .SelectMany(b => b.Works.Select(w => new { BookId = b.Id, WorkId = w.Id }))
            .ToListAsync(ct);

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
            // Alias rows just count their own works/books/series.
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

        return rows;
    }
}
