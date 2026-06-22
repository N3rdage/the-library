using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Updates an existing Copy's condition, acquired date, and notes.</summary>
public sealed record UpdateCopy(int CopyId, BookCondition Condition, DateTime? DateAcquired, string? Notes) : ICommand;

public sealed class UpdateCopyHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<UpdateCopy>
{
    public async Task HandleAsync(UpdateCopy command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var copy = await db.Copies.FindAsync([command.CopyId], ct)
            ?? throw new NotFoundException($"Copy {command.CopyId} not found.");

        copy.UpdateDetails(command.Condition, command.DateAcquired, command.Notes);
        await db.SaveChangesAsync(ct);
    }
}
