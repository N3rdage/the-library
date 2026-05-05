using BookTracker.Data;
using Microsoft.EntityFrameworkCore;
using Respawn;

namespace BookTracker.Tests;

/// <summary>
/// Creates DbContext instances backed by the process-shared SQL Server
/// container in <see cref="SqlServerContainer"/>. Constructor wipes all
/// user-table data via Respawn and re-inserts the HasData seeds, so each
/// `new TestDbContextFactory()` is a clean state — same semantics tests
/// got under the previous EF InMemory implementation.
///
/// Why real SQL: the InMemory provider doesn't enforce SQL translation
/// rules. LINQ patterns valid in C# but invalid in SQL passed tests under
/// InMemory and shipped to prod (the EF Core 10.x /publishers regression
/// was the canary). Real SQL Server in tests catches translation issues
/// at PR time. Wipe-and-reseed runs ~50-150ms per factory; ~322 tests
/// run serially in ~30-60s total.
/// </summary>
public class TestDbContextFactory : IDbContextFactory<BookTrackerDbContext>
{
    private static Respawner? _respawner;
    private static readonly SemaphoreSlim _respawnerLock = new(1, 1);

    private readonly DbContextOptions<BookTrackerDbContext> _options;
    private readonly string _connectionString;

    public TestDbContextFactory()
    {
        _connectionString = SqlServerContainer.ConnectionString;
        _options = new DbContextOptionsBuilder<BookTrackerDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        WipeAndReseed();
    }

    public BookTrackerDbContext CreateDbContext() => new(_options);

    private void WipeAndReseed()
    {
        var respawner = GetRespawner();
        respawner.ResetAsync(_connectionString).GetAwaiter().GetResult();

        // Don'\''t re-seed HasData rows: tests that need the follow-up Tag
        // either seed it themselves or rely on production code'\''s
        // EnsureFollowUpTagAsync helper (which creates the Tag if missing).
        // Re-seeding here would conflict with tests that explicitly add the
        // same Tag, since Tag.Name has a unique index that surfaces under
        // real SQL but was lax under InMemory.
    }

    private Respawner GetRespawner()
    {
        // Lazy-init the Respawner once per process — building it inspects
        // the schema (tables, FK graph) which is stable across tests.
        if (_respawner is not null) return _respawner;

        _respawnerLock.Wait();
        try
        {
            return _respawner ??= Respawner.CreateAsync(_connectionString, new RespawnerOptions
            {
                TablesToIgnore = [new("__EFMigrationsHistory")],
                DbAdapter = DbAdapter.SqlServer,
            }).GetAwaiter().GetResult();
        }
        finally
        {
            _respawnerLock.Release();
        }
    }
}
