using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Publishers;

// Read-model for the /publishers list. Relocated from PublisherListViewModel's
// inline DbContext read in PR6b-2. One row per Publisher with its edition count,
// ordered by name.
public sealed record GetPublisherList : IQuery<IReadOnlyList<PublisherRow>>;

public record PublisherRow(int Id, string Name, int EditionCount);

public sealed class GetPublisherListHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetPublisherList, IReadOnlyList<PublisherRow>>
{
    public async Task<IReadOnlyList<PublisherRow>> HandleAsync(GetPublisherList query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // OrderBy before Select so EF orders on the source entity column, not on
        // a property of a constructed PublisherRow. EF Core 10.x can't translate
        // OrderBy-on-record-projection when the record's constructor includes a
        // navigation aggregate (here `p.Editions.Count`) — it tries to invoke
        // the constructor inside the ORDER BY and fails. Anonymous types still
        // translate fine because EF maps property names back to source columns;
        // record positional constructors break that mapping.
        return await db.Publishers
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PublisherRow(p.Id, p.Name, p.Editions.Count))
            .ToListAsync(ct);
    }
}
