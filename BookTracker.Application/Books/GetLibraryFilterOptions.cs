using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Read-model for the Library filter dropdowns (genres tree-ordered, tags,
// series, author names). Relocated from BookListViewModel.LoadFilterOptionsAsync
// in PR6b-4.
public sealed record GetLibraryFilterOptions : IQuery<LibraryFilterOptions>;

public record LibraryFilterOptions(
    IReadOnlyList<GenreOption> Genres,
    IReadOnlyList<TagOption> Tags,
    IReadOnlyList<SeriesOption> Series,
    IReadOnlyList<string> Authors);

public sealed class GetLibraryFilterOptionsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetLibraryFilterOptions, LibraryFilterOptions>
{
    public async Task<LibraryFilterOptions> HandleAsync(GetLibraryFilterOptions query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var genres = await db.Genres
            .OrderBy(g => g.Name)
            .Select(g => new GenreOption(g.Id, g.Name, g.ParentGenreId))
            .ToListAsync(ct);

        // Flatten to parent-then-its-children order for the dropdown.
        var ordered = new List<GenreOption>();
        foreach (var parent in genres.Where(g => g.ParentGenreId is null).OrderBy(g => g.Name))
        {
            ordered.Add(parent);
            ordered.AddRange(genres.Where(g => g.ParentGenreId == parent.Id).OrderBy(g => g.Name));
        }

        var tags = await db.Tags
            .OrderBy(t => t.Name)
            .Select(t => new TagOption(t.Id, t.Name))
            .ToListAsync(ct);

        var series = await db.Series
            .OrderBy(s => s.Name)
            .Select(s => new SeriesOption(s.Id, s.Name))
            .ToListAsync(ct);

        // Every Author entity (including pen names) by name, so the user can
        // filter by either the canonical or an alias.
        var authors = await db.Authors
            .OrderBy(a => a.Name)
            .Select(a => a.Name)
            .ToListAsync(ct);

        return new LibraryFilterOptions(ordered, tags, series, authors);
    }
}
