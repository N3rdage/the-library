using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Duplicates;

/// <summary>Deletes a dismissal so its pair returns to the active list.
/// No-op when the dismissal no longer exists.</summary>
public sealed record RestoreDismissedDuplicate(int IgnoredDuplicateId) : ICommand;

public sealed class RestoreDismissedDuplicateHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RestoreDismissedDuplicate>
{
    public async Task HandleAsync(RestoreDismissedDuplicate command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.IgnoredDuplicates.FirstOrDefaultAsync(d => d.Id == command.IgnoredDuplicateId, ct);
        if (row is null) return;
        db.IgnoredDuplicates.Remove(row);
        await db.SaveChangesAsync(ct);
    }
}
