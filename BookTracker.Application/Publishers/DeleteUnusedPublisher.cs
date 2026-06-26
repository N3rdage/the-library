using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Publishers;

/// <summary>Deletes a publisher that no editions reference. Refuses (with a
/// message steering to merge) if any editions still point at it —
/// <c>Edition.PublisherId</c> is <c>OnDelete.Restrict</c>, so the delete would
/// otherwise blow up at the DB. Lifted from
/// PublisherListViewModel.DeleteUnusedAsync (PR6b-2).</summary>
public sealed record DeleteUnusedPublisher(int PublisherId) : ICommand<PublisherAdminResult>;

public sealed class DeleteUnusedPublisherHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<DeleteUnusedPublisher, PublisherAdminResult>
{
    public async Task<PublisherAdminResult> HandleAsync(DeleteUnusedPublisher command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var publisher = await db.Publishers
            .Include(p => p.Editions)
            .FirstOrDefaultAsync(p => p.Id == command.PublisherId, ct);
        if (publisher is null) return PublisherAdminResult.NoOp;

        if (publisher.Editions.Count > 0)
        {
            var n = publisher.Editions.Count;
            return PublisherAdminResult.Refused(
                $"Can't delete \"{publisher.Name}\" — {n} edition{(n == 1 ? "" : "s")} still reference it. Merge it into another publisher instead.");
        }

        var name = publisher.Name;
        db.Publishers.Remove(publisher);
        await db.SaveChangesAsync(ct);
        return PublisherAdminResult.Done($"Deleted unused publisher \"{name}\".");
    }
}
