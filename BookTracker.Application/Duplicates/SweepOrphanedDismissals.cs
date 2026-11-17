using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Duplicates;

/// <summary>Deletes every dismissal whose lower or higher id no longer names an
/// existing Author, Work, Book or Edition (the table is polymorphic, so no FK
/// cascades for it). The /duplicates page dispatches this before
/// <see cref="GetDuplicateReport"/>.</summary>
public sealed record SweepOrphanedDismissals : ICommand;

public sealed class SweepOrphanedDismissalsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<SweepOrphanedDismissals>
{
    public async Task HandleAsync(SweepOrphanedDismissals command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await db.IgnoredDuplicates.ToListAsync(ct);
        if (rows.Count == 0) return;

        var authorIds = await db.Authors.Select(a => a.Id).ToHashSetAsync(ct);
        var workIds = await db.Works.Select(w => w.Id).ToHashSetAsync(ct);
        var bookIds = await db.Books.Select(b => b.Id).ToHashSetAsync(ct);
        var editionIds = await db.Editions.Select(e => e.Id).ToHashSetAsync(ct);

        var orphans = rows.Where(r =>
        {
            var ids = r.EntityType switch
            {
                DuplicateEntityType.Author => authorIds,
                DuplicateEntityType.Work => workIds,
                DuplicateEntityType.Book => bookIds,
                DuplicateEntityType.Edition => editionIds,
                _ => null
            };
            return ids is null || !ids.Contains(r.LowerId) || !ids.Contains(r.HigherId);
        }).ToList();

        if (orphans.Count > 0)
        {
            db.IgnoredDuplicates.RemoveRange(orphans);
            await db.SaveChangesAsync(ct);
        }
    }
}
