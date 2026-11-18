using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Ai;

// Read-model for the AI book-advisor prompt: the whole library by title, each
// book with its works' author names joined ("A, B"), its rating and the
// distinct genre names across its works.
//
// Author names come through Work.Authors, the role-blind skip navigation, so
// an editor or translator credited on a work is listed as an author too.
public sealed record GetLibraryProfileForAdvisor : IQuery<IReadOnlyList<AdvisorBook>>;

public record AdvisorBook(string Title, string Author, int Rating, IReadOnlyList<string> Genres);

public sealed class GetLibraryProfileForAdvisorHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetLibraryProfileForAdvisor, IReadOnlyList<AdvisorBook>>
{
    public async Task<IReadOnlyList<AdvisorBook>> HandleAsync(GetLibraryProfileForAdvisor query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Books
            .AsNoTracking()
            .OrderBy(b => b.Title)
            .Select(b => new
            {
                b.Title,
                Author = string.Join(", ", b.Works.SelectMany(w => w.Authors.Select(a => a.Name)).Distinct()),
                b.Rating,
                Genres = b.Works.SelectMany(w => w.Genres).Select(g => g.Name).Distinct().ToList(),
            })
            .ToListAsync(ct);
        return rows.Select(r => new AdvisorBook(r.Title, r.Author, r.Rating, r.Genres)).ToList();
    }
}
