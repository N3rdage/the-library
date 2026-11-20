using BookTracker.Shared.Catalog;
using BookTracker.Shared.Online;
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

    /// <summary>Calls GET /api/online/isbn/{isbn} with the cached bearer
    /// token: what the providers know about an ISBN the cache does not
    /// hold, and whether the library owns that work. Returns null when no
    /// provider knows the ISBN (404) and on any other failure — the Result
    /// page then keeps its offline answer, so this never throws.</summary>
    Task<OnlineIsbnLookup?> LookupIsbnOnlineAsync(string isbn, CancellationToken ct = default);

    /// <summary>Calls GET /api/online/author-works?author={name} with the
    /// cached bearer token: the author's Open Library bibliography, each work
    /// flagged with the owned Book when the library holds it. Returns null on
    /// any failure — the Author-works page then keeps its owned list, so this
    /// never throws.</summary>
    Task<OnlineAuthorWorks?> GetAuthorWorksOnlineAsync(string author, CancellationToken ct = default);

    /// <summary>Calls GET /api/online/work-editions?workKey={key} with the
    /// cached bearer token: the other editions of an Open Library work, each
    /// flagged with the owned Book when the library holds that ISBN. Returns
    /// null on any failure — the Result page then keeps its disabled row, so
    /// this never throws.</summary>
    Task<OnlineWorkEditions?> GetWorkEditionsOnlineAsync(string workKey, CancellationToken ct = default);
}
