using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Genres;

// Read-model for the genre picker (chips, typeahead and tree on the Work
// forms): every genre by name, flat, with its parent's id and name so the
// picker can label "Parent / Leaf" and build the tree. Relocated from
// MudGenrePickerViewModel's inline DbContext read (#264).
public sealed record GetGenreOptions : IQuery<IReadOnlyList<GenrePickerOption>>;

public record GenrePickerOption(int Id, string Name, int? ParentGenreId, string? ParentName);

public sealed class GetGenreOptionsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetGenreOptions, IReadOnlyList<GenrePickerOption>>
{
    public async Task<IReadOnlyList<GenrePickerOption>> HandleAsync(GetGenreOptions query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Genres
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GenrePickerOption(
                g.Id,
                g.Name,
                g.ParentGenreId,
                g.ParentGenre == null ? null : g.ParentGenre.Name))
            .ToListAsync(ct);
    }
}
