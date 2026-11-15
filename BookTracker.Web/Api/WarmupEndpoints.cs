using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Api;

// GET /warmup — slot-swap warmup probe. Azure App Service pings this
// URL during deployment-slot swaps when `WEBSITE_SWAP_WARMUP_PING_PATH`
// is set (see infra/modules/app-config.bicep). The probe runs *before*
// Azure promotes the slot, so all the cold-start cost we'd otherwise
// inflict on the first real user lands here instead:
//
//   - JIT compilation of Minimal API + DI graph build
//   - SQL connection pool establishment
//   - Managed-identity AAD token acquisition for SQL (the expensive one)
//   - EF metadata initialisation
//
// We deliberately do a trivial DB read (`Books.Take(1)`) so the SQL
// pool actually gets a connection on the AAD-auth path — without the
// read, the warmup would warm Kestrel + Minimal API only and the first
// real user would still pay the ~30-40s AAD-token cost.
//
// The read happens once per process. WarmupState remembers that this
// instance has warmed its SQL path, and every later probe returns the
// constant without touching the database. The route is anonymous (see
// below), so without the flag an internet-wide flood of GET /warmup
// would spend DTUs and pool connections before any sign-in (LIB-013).
// The flag resets on restart, which is exactly when a warm-up is
// meaningful again, so the swap probe keeps doing its job.
//
// Auth: this is the one route excluded from Easy Auth (alongside the
// PWA manifest + service worker). Azure's warmup probe is anonymous —
// if it required AAD it would 302 to login and never actually warm
// the .NET app. See `publicPaths` in infra/modules/app-config.bicep.
// The endpoint returns a fixed plaintext string and reveals no
// business data, so anonymous access is harmless.
public static class WarmupEndpoints
{
    public static IEndpointRouteBuilder MapWarmupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/warmup", HandleAsync);
        return app;
    }

    // The handler body, internal so the unit test can drive it without a
    // host: with a warm state it must return without asking the factory
    // for a context.
    internal static async Task<IResult> HandleAsync(
        WarmupState state,
        IDbContextFactory<BookTrackerDbContext> dbFactory,
        CancellationToken ct)
    {
        if (!state.IsWarm)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // Trivial query — establishes the connection pool entry,
            // forces AAD token acquisition, exercises the EF metadata
            // path. Returning just the Id keeps the payload tiny and
            // avoids materialising any Book columns.
            _ = await db.Books.Take(1).Select(b => b.Id).ToListAsync(ct);
            state.MarkWarm();
        }

        return Results.Text("warm", "text/plain");
    }
}

/// <summary>
/// Process-wide "this instance has warmed its SQL path" flag for
/// <see cref="WarmupEndpoints"/>. Registered as a singleton; starts cold on
/// every process start. Two probes racing on a cold instance may both touch
/// the database once, which is harmless.
/// </summary>
public sealed class WarmupState
{
    private int _warm;

    public bool IsWarm => Volatile.Read(ref _warm) == 1;

    public void MarkWarm() => Volatile.Write(ref _warm, 1);
}
