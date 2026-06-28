using BookTracker.Application.Authors;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the eager-create Author command (TD-15a). The
// author/contributor pickers dispatch this on commit so the row exists
// immediately; it's an idempotent find-or-create returning the id.
[Trait("Category", TestCategories.Integration)]
public class CreateAuthorHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task CreateAuthor_newName_insertsTrimmedAndReturnsId()
    {
        var id = await new CreateAuthorHandler(_factory).HandleAsync(new CreateAuthor("  Brandon Sanderson  "));

        await using var db = _factory.CreateDbContext();
        var saved = await db.Authors.FindAsync(id);
        Assert.NotNull(saved);
        Assert.Equal("Brandon Sanderson", saved!.Name); // trimmed
        Assert.Equal(1, db.Authors.Count(a => a.Name == "Brandon Sanderson"));
    }

    [Fact]
    public async Task CreateAuthor_existingName_returnsExistingIdNoDuplicate()
    {
        var first = await new CreateAuthorHandler(_factory).HandleAsync(new CreateAuthor("Terry Pratchett"));
        var second = await new CreateAuthorHandler(_factory).HandleAsync(new CreateAuthor("terry pratchett")); // CI clash

        Assert.Equal(first, second); // idempotent — resolves to the same row
        await using var db = _factory.CreateDbContext();
        Assert.Equal(1, db.Authors.Count(a => a.Name == "Terry Pratchett")); // not duplicated
    }
}
