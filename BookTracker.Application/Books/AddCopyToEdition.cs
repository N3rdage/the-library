using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Adds a Copy to an existing Edition. Returns the new Copy's id.</summary>
public sealed record AddCopyToEdition(int EditionId, BookCondition Condition, DateTime? DateAcquired, string? Notes) : ICommand<int>;

public sealed class AddCopyToEditionHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<AddCopyToEdition, int>
{
    public async Task<int> HandleAsync(AddCopyToEdition command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var edition = await db.Editions.FindAsync([command.EditionId], ct)
            ?? throw new NotFoundException($"Edition {command.EditionId} not found.");

        var copy = edition.AddCopy(command.Condition, command.DateAcquired, command.Notes);
        await db.SaveChangesAsync(ct);
        return copy.Id;
    }
}
