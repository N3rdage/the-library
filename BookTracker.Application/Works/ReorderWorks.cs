using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

/// <summary>Rewrites the per-book display order of a Book's Works in one save.
/// <paramref name="OrderedWorkIds"/> is the full desired order (0-based on
/// apply) — both the drag-and-drop and the "type a position" gestures reduce to
/// "here's the new full sequence". Works present on the book but missing from
/// the list (a stale client that raced an add/remove) keep their relative order
/// and sort after the listed ones, so nothing collides or silently drops.
/// Returns false only when the book is gone. Idempotent: re-sending the current
/// order changes nothing (and so doesn't bump the sync watermark).</summary>
public sealed record ReorderWorks(int BookId, IReadOnlyList<int> OrderedWorkIds) : ICommand<bool>;

public sealed class ReorderWorksHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<ReorderWorks, bool>
{
    public async Task<bool> HandleAsync(ReorderWorks command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books
            .Include(b => b.BookWorks)
            .FirstOrDefaultAsync(b => b.Id == command.BookId, ct);
        if (book is null) return false;

        // Target position for each requested work id (first occurrence wins if a
        // caller ever double-lists an id).
        var targetIndex = new Dictionary<int, int>();
        for (var i = 0; i < command.OrderedWorkIds.Count; i++)
            targetIndex.TryAdd(command.OrderedWorkIds[i], i);

        // Assign 0..n-1 to the listed works; anything left over (not in the list)
        // is appended after, preserving its old relative order. Walking in old
        // Order keeps that leftover ordering deterministic.
        var next = command.OrderedWorkIds.Count;
        foreach (var bw in book.BookWorks.OrderBy(bw => bw.Order).ToList())
            bw.Order = targetIndex.TryGetValue(bw.WorkId, out var idx) ? idx : next++;

        // Only rows whose Order actually changed are Modified — a no-op reorder
        // writes nothing, so BookUpdatedAtInterceptor won't bump UpdatedAt.
        await db.SaveChangesAsync(ct);
        return true;
    }
}
