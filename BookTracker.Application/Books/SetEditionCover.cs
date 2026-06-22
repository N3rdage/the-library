using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Persists a new cover URL for an Edition. The blob upload itself
/// (reading the browser file, size validation, pushing to cover storage)
/// stays in the Web layer — that storage service is a Web concern and can't
/// move below the application layer without dragging the cover subsystem with
/// it. This handler owns only the persistence half.</summary>
public sealed record SetEditionCover(int EditionId, string Url, bool IsUserSupplied = true);

public sealed class SetEditionCoverHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public async Task HandleAsync(SetEditionCover command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var edition = await db.Editions.FindAsync([command.EditionId], ct)
            ?? throw new NotFoundException($"Edition {command.EditionId} not found.");

        edition.SetCover(command.Url, command.IsUserSupplied);
        await db.SaveChangesAsync(ct);
    }
}
