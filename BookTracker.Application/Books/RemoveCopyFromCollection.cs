using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Marks a Copy as gone from the collection (donated, lost, damaged,
/// sold) on the given date, with an optional note. The copy and its loan
/// history stay. Throws <see cref="NotFoundException"/> when the copy is gone,
/// and the entity's DomainRuleException when the reason is Owned, the copy is
/// already removed or lent out, or the note is too long.</summary>
public sealed record RemoveCopyFromCollection(int CopyId, CopyDisposition Reason, DateOnly RemovedOn, string? Note) : ICommand;

public sealed class RemoveCopyFromCollectionHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RemoveCopyFromCollection>
{
    public async Task HandleAsync(RemoveCopyFromCollection command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Load the Loans so the open-loan check sees the real set.
        var copy = await db.Copies
            .Include(c => c.Loans)
            .FirstOrDefaultAsync(c => c.Id == command.CopyId, ct)
            ?? throw new NotFoundException($"Copy {command.CopyId} not found.");

        copy.RemoveFromCollection(command.Reason, command.RemovedOn, command.Note);
        await db.SaveChangesAsync(ct);
    }
}
