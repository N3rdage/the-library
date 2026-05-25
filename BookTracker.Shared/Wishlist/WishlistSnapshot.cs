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
    DateTime DateAdded,
    // CoverUrl: upstream cover image from the search-and-add flow
    // (PR B). Lightweight — not mirrored to blob storage. Null for
    // legacy QuickAdd rows + series-driven stubs (PR C).
    string? CoverUrl = null,
    // Isbns: every known ISBN for this wishlisted book. Unions the
    // legacy single Isbn column with the PR B WishlistItemIsbn rows,
    // deduped. Backs the Bookshelf scan-flag — ANY matching ISBN
    // means "on your wishlist".
    IReadOnlyList<string>? Isbns = null);
