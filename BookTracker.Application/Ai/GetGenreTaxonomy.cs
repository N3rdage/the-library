using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Ai;

// Read-model for the AI genre-suggestion prompt: every genre by name with its
// parent's name, so the prompt can list the preset taxonomy ("Space Opera
// (sub-genre of Science Fiction)") and the reply can be checked against the
// exact names. Only the two names cross the wire.
public sealed record GetGenreTaxonomy : IQuery<IReadOnlyList<GenreTaxonomyEntry>>;

public record GenreTaxonomyEntry(string Name, string? ParentName);

public sealed class GetGenreTaxonomyHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetGenreTaxonomy, IReadOnlyList<GenreTaxonomyEntry>>
{
    public async Task<IReadOnlyList<GenreTaxonomyEntry>> HandleAsync(GetGenreTaxonomy query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Genres
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GenreTaxonomyEntry(
                g.Name,
                g.ParentGenre != null ? g.ParentGenre.Name : null))
            .ToListAsync(ct);
    }
}
