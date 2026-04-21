using BookTracker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookTracker.Tests;

/// <summary>
/// Creates in-memory DbContext instances for unit testing.
/// Each factory instance uses a unique database name so tests don't interfere.
/// Transactions are silently no-oped by InMemory; we suppress the warning so
/// services that wrap work in BeginTransactionAsync still pass under tests.
/// </summary>
public class TestDbContextFactory : IDbContextFactory<BookTrackerDbContext>
{
    private readonly DbContextOptions<BookTrackerDbContext> _options;

    public TestDbContextFactory(string? databaseName = null)
    {
        databaseName ??= Guid.NewGuid().ToString();
        _options = new DbContextOptionsBuilder<BookTrackerDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    public BookTrackerDbContext CreateDbContext()
    {
        return new BookTrackerDbContext(_options);
    }
}
