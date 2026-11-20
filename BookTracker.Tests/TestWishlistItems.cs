using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Seed builder for WishlistItem rows. Every seed builds through here, so
// WishlistItem's private setters need only this body to reach the internal
// constructor (InternalsVisibleTo).
//
// The body stores exactly what it is given, nothing trimmed or normalised, and
// an omitted argument leaves the entity default. Unlike WishlistItem.SetIsbns,
// the legacy `isbn` column and the `isbns` table are written independently, so
// a seed can stand for a legacy row (isbn only), a table-only row (isbns only)
// or any overlap of the two.
public static class TestWishlistItems
{
    public static WishlistItem Item(
        string title,
        string author,
        WishlistPriority priority = WishlistPriority.Medium,
        string? isbn = null,
        IEnumerable<string>? isbns = null,
        string? coverUrl = null,
        DateTime? dateAdded = null,
        Series? series = null,
        int? seriesId = null,
        int? seriesOrder = null)
    {
        return new WishlistItem(
            title, author, priority, isbn, isbns, coverUrl, dateAdded, series, seriesId, seriesOrder);
    }
}
