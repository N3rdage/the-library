using BookTracker.Data;
using BookTracker.Shared.Wishlist;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services.Wishlist;

// Wishlist projection consumed by the /api/wishlist-snapshot endpoint.
// Same shape as CatalogSnapshotService — slim, read-only, JSON-on-the-
// wire, populated by a single query. See docs/mobile-app-design.md
// for the consumer-side picture (BookTracker.Mobile MAUI app).
//
// Wishlist mutations (add / remove / "bought") stay online-only on the
// Web app; the mobile companion is read-only in v1.
public interface IWishlistSnapshotService
{
    Task<WishlistSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

public class WishlistSnapshotService(
    IDbContextFactory<BookTrackerDbContext> dbFactory) : IWishlistSnapshotService
{
    public async Task<WishlistSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Order: priority desc (High first), then DateAdded asc. Matches
        // "what to buy next" semantics — high-priority items the user
        // added a while ago bubble to the top. Mobile UI can re-sort
        // client-side if it wants other orderings.
        var items = await db.WishlistItems
            .AsNoTracking()
            .OrderByDescending(w => w.Priority)
            .ThenBy(w => w.DateAdded)
            .Select(w => new WishlistItemSnapshot(
                w.Id,
                w.Title,
                w.Author,
                w.Priority.ToString(),
                w.Isbn,
                w.SeriesId,
                w.SeriesOrder,
                w.DateAdded))
            .ToListAsync(ct);

        return new WishlistSnapshot(
            BuildInfo.ShortSha ?? "dev",
            DateTime.UtcNow,
            items);
    }
}
