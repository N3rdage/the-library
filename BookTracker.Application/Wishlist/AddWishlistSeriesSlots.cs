using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookTracker.Application.Wishlist;

/// <summary>Bulk-adds placeholder wishlist stubs for missing slots in a series.
/// De-dupes + filters the requested slots to positives, skips slots already
/// wishlisted for this series (so re-runs are idempotent), and creates one stub
/// per new slot via <see cref="WishlistItem.CreateSeriesSlot"/>. Returns the
/// count actually added; a soft no-op (0) if the series is gone or every
/// requested slot is already covered.</summary>
public sealed record AddWishlistSeriesSlots(int SeriesId, IReadOnlyList<int> Slots) : ICommand<int>;

public sealed class AddWishlistSeriesSlotsHandler(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    ILogger<AddWishlistSeriesSlotsHandler> logger)
    : ICommandHandler<AddWishlistSeriesSlots, int>
{
    public async Task<int> HandleAsync(AddWishlistSeriesSlots command, CancellationToken ct = default)
    {
        var deduped = (command.Slots ?? [])
            .Where(s => s > 0)
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        if (deduped.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var series = await db.Series.FindAsync([command.SeriesId], ct);
        if (series is null)
        {
            logger.LogWarning("AddWishlistSeriesSlots called with unknown SeriesId {SeriesId}", command.SeriesId);
            return 0;
        }

        // Existing wishlist rows for this series + the requested slots — one
        // query so the dedup pass doesn't N+1.
        var alreadyWishlisted = await db.WishlistItems
            .Where(w => w.SeriesId == command.SeriesId && w.SeriesOrder != null && deduped.Contains(w.SeriesOrder!.Value))
            .Select(w => w.SeriesOrder!.Value)
            .ToListAsync(ct);
        var alreadySet = alreadyWishlisted.ToHashSet();

        var added = 0;
        foreach (var slot in deduped)
        {
            if (alreadySet.Contains(slot)) continue;
            db.WishlistItems.Add(WishlistItem.CreateSeriesSlot(command.SeriesId, series.Name, slot, series.Author));
            added++;
        }

        if (added == 0) return 0;
        await db.SaveChangesAsync(ct);
        return added;
    }
}
