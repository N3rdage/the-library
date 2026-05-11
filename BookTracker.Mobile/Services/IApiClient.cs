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
    /// path fails. Returns the deserialised CatalogSnapshot.</summary>
    Task<CatalogSnapshot> GetCatalogSnapshotAsync(CancellationToken ct = default);
}
