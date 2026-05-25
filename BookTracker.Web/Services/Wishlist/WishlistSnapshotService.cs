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
        //
        // CoverUrl + Isbns added 2026-05-25 alongside PR B's schema
        // additions. Isbns unions the legacy single Isbn column with
        // the per-item WishlistItemIsbn rows (deduped, case-insensitive)
        // so both shapes of wishlist row work the same way for the
        // Bookshelf scan-flag.
        var raw = await db.WishlistItems
            .AsNoTracking()
            .Include(w => w.Isbns)
            .OrderByDescending(w => w.Priority)
            .ThenBy(w => w.DateAdded)
            .Select(w => new
            {
                w.Id,
                w.Title,
                w.Author,
                w.Priority,
                w.Isbn,
                w.SeriesId,
                w.SeriesOrder,
                w.DateAdded,
                w.CoverUrl,
                IsbnRows = w.Isbns.Select(i => i.Isbn).ToList(),
            })
            .ToListAsync(ct);

        var items = raw
            .Select(w => new WishlistItemSnapshot(
                w.Id,
                w.Title,
                w.Author,
                w.Priority.ToString(),
                w.Isbn,
                w.SeriesId,
                w.SeriesOrder,
                w.DateAdded,
                CoverUrl: w.CoverUrl,
                Isbns: UnionIsbns(w.Isbn, w.IsbnRows)))
            .ToList();

        return new WishlistSnapshot(
            BuildInfo.ShortSha ?? "dev",
            DateTime.UtcNow,
            items);
    }

    /// <summary>Union the legacy single-Isbn column with the per-row
    /// WishlistItemIsbn entries, deduped case-insensitively. Both
    /// shapes of wishlist row (QuickAdd legacy + search-and-add
    /// post-PR-B) flow through the same `Isbns` list on the wire.</summary>
    private static IReadOnlyList<string> UnionIsbns(string? legacy, IReadOnlyList<string> rows)
    {
        var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(legacy)) union.Add(legacy);
        foreach (var r in rows)
        {
            if (!string.IsNullOrWhiteSpace(r)) union.Add(r);
        }
        return union.ToList();
    }
}
