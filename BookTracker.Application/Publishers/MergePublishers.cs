using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Publishers;

/// <summary>Merges the source publisher into the target: reassigns every edition
/// to the target then deletes the source, in a single transaction so a partial
/// failure never leaves a half-merged state. Lifted from
/// PublisherListViewModel.MergeAsync (PR6b-2); mirrors the merge-command
/// pattern established in PR5.</summary>
public sealed record MergePublishers(int SourceId, int TargetId) : ICommand<PublisherAdminResult>;

public sealed class MergePublishersHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<MergePublishers, PublisherAdminResult>
{
    public async Task<PublisherAdminResult> HandleAsync(MergePublishers command, CancellationToken ct = default)
    {
        if (command.SourceId == command.TargetId) return PublisherAdminResult.NoOp;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var source = await db.Publishers
            .Include(p => p.Editions)
            .FirstOrDefaultAsync(p => p.Id == command.SourceId, ct);
        var target = await db.Publishers.FirstOrDefaultAsync(p => p.Id == command.TargetId, ct);
        if (source is null || target is null) return PublisherAdminResult.NoOp;

        var editionCount = source.Editions.Count;

        // Reassign then delete in a single transaction so we never leave a
        // half-merged state if SaveChanges fails partway.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var edition in source.Editions)
        {
            edition.PublisherId = command.TargetId;
        }
        db.Publishers.Remove(source);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return PublisherAdminResult.Done(
            $"Merged \"{source.Name}\" into \"{target.Name}\" — {editionCount} edition{(editionCount == 1 ? "" : "s")} reassigned.");
    }
}
