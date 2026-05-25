using BookTracker.Shared.Catalog;
using BookTracker.Shared.Wishlist;

namespace BookTracker.Mobile.Services;

// Thin HTTP wrapper that calls /api/* with a bearer token from
// IAuthService.
public interface IApiClient
{
    /// <summary>Calls GET /api/catalog-snapshot with the cached
    /// bearer token. Re-prompts for sign-in if the silent token
    /// path fails. Returns the deserialised CatalogSnapshot.
    ///
    /// When <paramref name="since"/> is non-null, sends it as the
    /// <c>?since=&lt;ISO 8601 UTC&gt;</c> query parameter — the
    /// response is a delta containing only Books with
    /// <c>UpdatedAt &gt; since</c> plus tombstones in
    /// <c>DeletedIds</c> for soft-deletes with
    /// <c>DeletedAt &gt; since</c>. When null, the response is a
    /// full snapshot.</summary>
    Task<CatalogSnapshot> GetCatalogSnapshotAsync(DateTime? since = null, CancellationToken ct = default);

    /// <summary>Calls GET /api/wishlist-snapshot with the cached
    /// bearer token. Returns the full wishlist as a flat list
    /// (no delta semantics — the wishlist is small enough that a
    /// full refresh every time is cheap). Used by the Bookshelf
    /// WishlistPage and by the scan-flag lookup on ScanPage.</summary>
    Task<WishlistSnapshot> GetWishlistSnapshotAsync(CancellationToken ct = default);
}
