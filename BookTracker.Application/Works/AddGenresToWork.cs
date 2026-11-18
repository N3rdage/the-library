using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

/// <summary>Adds the named genres to a book's primary (first) Work, keeping the
/// genres it already has — the AI assistant's "accept suggestions". Names with
/// no matching Genre row are ignored, and only the named genres are added (no
/// parent genres). Returns the Work's genre names after the change, or null
/// when the book or its primary Work is gone. Relocated from
/// AIAssistantViewModel.AcceptGenreSuggestionsAsync.</summary>
public sealed record AddGenresToWork(int BookId, IReadOnlyList<string> GenreNames)
    : ICommand<IReadOnlyList<string>?>;

public sealed class AddGenresToWorkHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<AddGenresToWork, IReadOnlyList<string>?>
{
    public async Task<IReadOnlyList<string>?> HandleAsync(AddGenresToWork command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var book = await db.Books
            .Include(b => b.Works).ThenInclude(w => w.Genres)
            .FirstOrDefaultAsync(b => b.Id == command.BookId, ct);
        var primaryWork = book?.Works.FirstOrDefault();
        if (primaryWork is null) return null;

        var names = command.GenreNames.ToList();
        var genresToAdd = await db.Genres
            .Where(g => names.Contains(g.Name))
            .ToListAsync(ct);

        // Union of the current genres and the new ones, in that order.
        var merged = primaryWork.Genres.ToList();
        merged.AddRange(genresToAdd.Where(g => merged.All(m => m.Id != g.Id)));
        primaryWork.SetGenres(merged);

        await db.SaveChangesAsync(ct);
        return primaryWork.Genres.Select(g => g.Name).ToList();
    }
}
