using BookTracker.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace BookTracker.Tests;

/// <summary>
/// Process-scoped SQL Server container backing all integration tests.
/// First access starts the container (Docker pull on cold cache, ~30-60s;
/// cached subsequent runs ~2-5s) and applies BookTracker.Data migrations
/// once. Container disposes on process exit via the AppDomain hook.
///
/// Sidesteps xUnit's per-collection fixture pattern so existing test
/// classes don't need [Collection] attributes — the container is a
/// process singleton, accessed by TestDbContextFactory on construction.
///
/// Trade-off: disabling parallel test execution (see AssemblyInfo) so
/// tests don't trample each other's data on the shared schema. Each test
/// gets a clean DB state via TestDbContextFactory's wipe-and-reseed.
/// </summary>
internal static class SqlServerContainer
{
    private static readonly Lazy<MsSqlContainer> _container = new(StartAndMigrate, isThreadSafe: true);

    public static string ConnectionString => _container.Value.GetConnectionString();

    private static MsSqlContainer StartAndMigrate()
    {
        var c = new MsSqlBuilder()
            .WithCleanUp(true)
            .Build();

        c.StartAsync().GetAwaiter().GetResult();

        // Apply migrations once to set up the schema. Subsequent tests wipe
        // data via TestDbContextFactory but leave the schema intact.
        var options = new DbContextOptionsBuilder<BookTrackerDbContext>()
            .UseSqlServer(c.GetConnectionString())
            .Options;
        using (var ctx = new BookTrackerDbContext(options))
        {
            ctx.Database.Migrate();
        }

        // Best-effort cleanup on process exit. Testcontainers' Ryuk reaper
        // will also clean up if this misses (e.g. process killed).
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { c.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* best-effort */ }
        };

        return c;
    }
}
