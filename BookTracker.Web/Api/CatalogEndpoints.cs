using System.Globalization;
using BookTracker.Application;
using BookTracker.Application.Catalog;
using BookTracker.Web.Services;

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
        // GET /api/catalog-snapshot[?since=<ISO 8601 UTC>] — slim catalog
        // JSON for the bookshop offline cache. Full snapshot when `since`
        // is absent; delta of Books with UpdatedAt > since when present
        // (Authors + Series are always full-listed regardless of since).
        // Service worker pre-caches the response; IndexedDB / SQLite
        // stores client-side. See docs/bookshop-mode-design.md.
        //
        // `since` is bound as a raw string so we can enforce UTC parsing
        // explicitly via DateTimeStyles.AdjustToUniversal — the default
        // DateTime binder in Minimal API treats values without timezone
        // suffix as Local, which would produce wrong-by-hours filtering
        // on a UTC-stored column.
        app.MapGet("/api/catalog-snapshot", async (
            string? since,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            DateTime? parsedSince = null;
            if (!string.IsNullOrWhiteSpace(since))
            {
                if (!DateTime.TryParse(
                        since,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out var parsed))
                {
                    return Results.BadRequest(
                        $"Invalid 'since' value: '{since}'. Expected ISO 8601 UTC (e.g. 2026-05-14T12:30:00Z).");
                }
                parsedSince = parsed;
            }

            // BuildInfo (deployed SHA) stays a host concern — passed into the
            // read-model query so the Application handler stays host-agnostic.
            var snapshot = await dispatcher.Query(
                new GetCatalogSnapshot(parsedSince, BuildInfo.ShortSha ?? "dev"), ct);
            return Results.Ok(snapshot);
        });

        return app;
    }
}
