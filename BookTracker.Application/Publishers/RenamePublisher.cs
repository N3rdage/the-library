using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Publishers;

/// <summary>Renames a publisher. Refuses (with a message steering to merge) if
/// another publisher already has the trimmed name — <c>Publisher.Name</c> is
/// uniquely indexed. Lifted from PublisherListViewModel.RenameAsync (PR6b-2).</summary>
public sealed record RenamePublisher(int PublisherId, string NewName) : ICommand<PublisherAdminResult>;

public sealed class RenamePublisherHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RenamePublisher, PublisherAdminResult>
{
    public async Task<PublisherAdminResult> HandleAsync(RenamePublisher command, CancellationToken ct = default)
    {
        var trimmed = command.NewName.Trim();
        if (string.IsNullOrEmpty(trimmed)) return PublisherAdminResult.NoOp;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Id == command.PublisherId, ct);
        if (publisher is null) return PublisherAdminResult.NoOp;

        var clash = await db.Publishers.AnyAsync(p => p.Id != command.PublisherId && p.Name == trimmed, ct);
        if (clash)
        {
            return PublisherAdminResult.Refused(
                $"A publisher named \"{trimmed}\" already exists. Use the merge action to combine them.");
        }

        publisher.Name = trimmed;
        await db.SaveChangesAsync(ct);
        return PublisherAdminResult.Done($"Renamed to \"{trimmed}\".");
    }
}
