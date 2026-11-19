using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Publishers;

// Read-model for the publisher autocomplete on the edition forms (the Add/Edit
// Edition dialog and the Add page's edition section): every publisher by name.
// The forms cache the list and filter it in memory per keystroke. One query for
// what were two identical inline reads in EditionFormDialogViewModel and
// EditionFormViewModel (#264).
public sealed record GetPublisherOptions : IQuery<IReadOnlyList<PublisherOption>>;

public record PublisherOption(int Id, string Name);

public sealed class GetPublisherOptionsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetPublisherOptions, IReadOnlyList<PublisherOption>>
{
    public async Task<IReadOnlyList<PublisherOption>> HandleAsync(GetPublisherOptions query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Publishers
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PublisherOption(p.Id, p.Name))
            .ToListAsync(ct);
    }
}
