using BookTracker.Application.Series;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the eager-create Series command (TD-15a). Accepting a
// "new series" suggestion dispatches this so the row exists at the accept
// gesture; it's an idempotent find-or-create returning the id (or null for a
// blank name), distinct from the strict CreateSeries command.
[Trait("Category", TestCategories.Integration)]
public class EnsureSeriesHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task EnsureSeries_newName_insertsTrimmedSeriesAndReturnsId()
    {
        var id = await new EnsureSeriesHandler(_factory).HandleAsync(new EnsureSeries("  The Stormlight Archive  "));

        Assert.NotNull(id);
        await using var db = _factory.CreateDbContext();
        var saved = await db.Series.FindAsync(id);
        Assert.NotNull(saved);
        Assert.Equal("The Stormlight Archive", saved!.Name); // trimmed
        Assert.Equal(SeriesType.Series, saved.Type); // resolver default — not Collection
        Assert.Equal(1, db.Series.Count(s => s.Name == "The Stormlight Archive"));
    }

    [Fact]
    public async Task EnsureSeries_existingName_returnsExistingIdNoDuplicate()
    {
        var first = await new EnsureSeriesHandler(_factory).HandleAsync(new EnsureSeries("Discworld"));
        var second = await new EnsureSeriesHandler(_factory).HandleAsync(new EnsureSeries("discworld")); // CI clash

        Assert.Equal(first, second); // idempotent — resolves to the same row
        await using var db = _factory.CreateDbContext();
        Assert.Equal(1, db.Series.Count(s => s.Name == "Discworld")); // not duplicated
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EnsureSeries_blankName_returnsNullAndInsertsNothing(string? name)
    {
        var id = await new EnsureSeriesHandler(_factory).HandleAsync(new EnsureSeries(name));

        Assert.Null(id);
        await using var db = _factory.CreateDbContext();
        Assert.Equal(0, db.Series.Count());
    }
}
