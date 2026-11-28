using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Brings a removed Copy back into the collection, clearing its
/// removal date and note. Throws <see cref="NotFoundException"/> when the copy
/// is gone, and the entity's DomainRuleException when it is already
/// owned.</summary>
public sealed record RestoreCopyToCollection(int CopyId) : ICommand;

public sealed class RestoreCopyToCollectionHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RestoreCopyToCollection>
{
    public async Task HandleAsync(RestoreCopyToCollection command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var copy = await db.Copies.FindAsync([command.CopyId], ct)
            ?? throw new NotFoundException($"Copy {command.CopyId} not found.");

        copy.RestoreToCollection();
        await db.SaveChangesAsync(ct);
    }
}
