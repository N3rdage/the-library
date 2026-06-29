using BookTracker.Application.Books;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the eager-create Publisher command (TD-15a). The
// publisher autocompletes dispatch this on commit so the row exists
// immediately; it's an idempotent find-or-create returning the id (or null
// for a blank name, mirroring PublisherResolver).
[Trait("Category", TestCategories.Integration)]
public class CreatePublisherHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task CreatePublisher_newName_insertsTrimmedAndReturnsId()
    {
        var id = await new CreatePublisherHandler(_factory).HandleAsync(new CreatePublisher("  Gollancz  "));

        Assert.NotNull(id);
        await using var db = _factory.CreateDbContext();
        var saved = await db.Publishers.FindAsync(id);
        Assert.NotNull(saved);
        Assert.Equal("Gollancz", saved!.Name); // trimmed
        Assert.Equal(1, db.Publishers.Count(p => p.Name == "Gollancz"));
    }

    [Fact]
    public async Task CreatePublisher_existingName_returnsExistingIdNoDuplicate()
    {
        var first = await new CreatePublisherHandler(_factory).HandleAsync(new CreatePublisher("Penguin Books"));
        var second = await new CreatePublisherHandler(_factory).HandleAsync(new CreatePublisher("penguin books")); // CI clash

        Assert.Equal(first, second); // idempotent — resolves to the same row
        await using var db = _factory.CreateDbContext();
        Assert.Equal(1, db.Publishers.Count(p => p.Name == "Penguin Books")); // not duplicated
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatePublisher_blankName_returnsNullAndInsertsNothing(string? name)
    {
        var id = await new CreatePublisherHandler(_factory).HandleAsync(new CreatePublisher(name));

        Assert.Null(id);
        await using var db = _factory.CreateDbContext();
        Assert.Equal(0, db.Publishers.Count());
    }
}
