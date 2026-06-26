using BookTracker.Application.Series;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the /series list read-model handler, introduced in
// PR6b when SeriesListViewModel's inline DbContext read moved into
// GetSeriesList. Covers the name/author substring + type filters and the
// Works.Count projection. The badge/completion presentation helpers are
// covered by SeriesListViewModelTests.
[Trait("Category", TestCategories.Integration)]
public class GetSeriesListHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<IReadOnlyList<SeriesListItem>> Load(string? search = null, SeriesType? type = null) =>
        new GetSeriesListHandler(_factory).HandleAsync(new GetSeriesList(search, type));

    private async Task SeedAsync()
    {
        using var db = _factory.CreateDbContext();
        var pratchett = new Author { Name = "Terry Pratchett" };
        db.Authors.Add(pratchett);
        var discworld = new Series { Name = "Discworld", Author = "Terry Pratchett", Type = SeriesType.Collection, ExpectedCount = 41 };
        var foundation = new Series { Name = "Foundation", Author = "Isaac Asimov", Type = SeriesType.Series, ExpectedCount = 7 };
        db.Series.AddRange(discworld, foundation);
        db.Books.AddRange(
            new Book { Title = "Mort", Works = [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 4 }] },
            new Book { Title = "Reaper Man", Works = [new Work { Title = "Reaper Man", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 11 }] });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task NoFilters_ReturnsAllOrderedByName_WithWorkCount()
    {
        await SeedAsync();

        var items = await Load();

        Assert.Equal(2, items.Count);
        Assert.Equal("Discworld", items[0].Name);   // alphabetical
        Assert.Equal("Foundation", items[1].Name);
        Assert.Equal(2, items[0].WorkCount);
        Assert.Equal(0, items[1].WorkCount);
    }

    [Fact]
    public async Task SearchFilter_MatchesNameOrAuthorSubstring()
    {
        await SeedAsync();

        Assert.Single(await Load(search: "disc"));      // name
        Assert.Single(await Load(search: "Asimov"));    // author
        Assert.Empty(await Load(search: "Tolkien"));
    }

    [Fact]
    public async Task TypeFilter_RestrictsToMatchingType()
    {
        await SeedAsync();

        var collections = await Load(type: SeriesType.Collection);
        var only = Assert.Single(collections);
        Assert.Equal("Discworld", only.Name);

        var series = await Load(type: SeriesType.Series);
        Assert.Equal("Foundation", Assert.Single(series).Name);
    }
}
