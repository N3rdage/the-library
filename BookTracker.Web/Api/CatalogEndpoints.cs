using BookTracker.Web.Services.Catalog;

namespace BookTracker.Web.Api;

// First first-class API surface in the project. Convention going forward:
// each domain gets its own *Endpoints.cs static class with an extension
// method on IEndpointRouteBuilder; ProgramSetup.cs maps them all together.
// Single-endpoint pattern is fine for now — abstractions follow when a
// second endpoint joins the family.
//
// Auth: Easy Auth at the App Service platform layer gates every request,
// matching every other route in the app. There's deliberately no app-side
// authentication / authorization middleware (the project doesn't register
// AddAuthentication / AddAuthorization) — Easy Auth + the
// `globalValidation.requireAuthentication: true` setting in
// infra/modules/app-config.bicep handle the gate. See SECURITY-AUDIT.md
// (SEC-006) for the verification.
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/catalog-snapshot — slim catalog JSON for the bookshop
        // offline cache. ~150KB gzipped at the 3000+ books target. Service
        // worker pre-caches the response; IndexedDB stores client-side.
        // See docs/bookshop-mode-design.md for the full architecture.
        app.MapGet("/api/catalog-snapshot", async (
            ICatalogSnapshotService service,
            CancellationToken ct) =>
        {
            var snapshot = await service.GetSnapshotAsync(ct);
            return Results.Ok(snapshot);
        });

        return app;
    }
}
