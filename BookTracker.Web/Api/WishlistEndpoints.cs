using BookTracker.Web.Services.Wishlist;

namespace BookTracker.Web.Api;

// Second member of the *Endpoints.cs convention started by
// CatalogEndpoints.cs. Auth: same Easy Auth platform-layer gate as
// every other route in the app — see SECURITY-AUDIT.md (SEC-006)
// for the verification.
public static class WishlistEndpoints
{
    public static IEndpointRouteBuilder MapWishlistEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/wishlist-snapshot — slim wishlist JSON for the
        // future BookTracker.Mobile companion's offline read view.
        // Unlike the catalog snapshot (already consumed by /bookshop
        // today) this endpoint has no current Web caller; it ships
        // as the contract MAUI will code against. See
        // docs/mobile-app-design.md.
        app.MapGet("/api/wishlist-snapshot", async (
            IWishlistSnapshotService service,
            CancellationToken ct) =>
        {
            var snapshot = await service.GetSnapshotAsync(ct);
            return Results.Ok(snapshot);
        });

        return app;
    }
}
