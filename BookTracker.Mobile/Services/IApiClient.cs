using BookTracker.Shared.Catalog;

namespace BookTracker.Mobile.Services;

// Thin HTTP wrapper that calls /api/* with a bearer token from
// IAuthService. PR 3 only exposes one method — the smoke-test catalog
// fetch — but the shape generalises to the future ICatalogCache the
// MAUI app will populate from this output (PR 4 of the mobile arc).
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
}
