namespace BookTracker.Shared.Wishlist;

// Wire-format DTOs for GET /api/wishlist-snapshot. Read-only mirror
// of the user's wishlist for offline mobile consumption. Add /
// remove / "bought" actions stay online-only on the Web app — the
// mobile app reads but doesn't write in v1.

public record WishlistSnapshot(
    string Version,
    DateTime SyncedAt,
    IReadOnlyList<WishlistItemSnapshot> Items);

public record WishlistItemSnapshot(
    int Id,
    string Title,
    string Author,
    string Priority,
    string? Isbn,
    int? SeriesId,
    int? SeriesOrder,
    DateTime DateAdded);
