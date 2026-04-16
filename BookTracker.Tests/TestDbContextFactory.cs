using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests;

/// <summary>
/// Creates in-memory DbContext instances for unit testing.
/// Each factory instance uses a unique database name so tests don't interfere.
/// </summary>
public class TestDbContextFactory : IDbContextFactory<BookTrackerDbContext>
{
    private readonly DbContextOptions<BookTrackerDbContext> _options;

    public TestDbContextFactory(string? databaseName = null)
    {
        databaseName ??= Guid.NewGuid().ToString();
        _options = new DbContextOptionsBuilder<BookTrackerDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
    }

    public BookTrackerDbContext CreateDbContext()
    {
        return new BookTrackerDbContext(_options);
    }
}
