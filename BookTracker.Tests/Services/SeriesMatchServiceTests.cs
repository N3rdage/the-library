using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

public class SeriesMatchServiceTests
{
    [Fact]
    public async Task FindMatchAsync_AuthorWithExistingSeries_ReturnsSuggestion()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var series = new Series { Name = "Discworld", Author = "Terry Pratchett", Type = SeriesType.Collection };
            db.Series.Add(series);
            await db.SaveChangesAsync();
        }

        var service = new SeriesMatchService(factory);
        var match = await service.FindMatchAsync("The Colour of Magic", "Terry Pratchett");

        Assert.NotNull(match);
        Assert.Equal(MatchReason.AuthorMatch, match.Reason);
        Assert.Equal("Discworld", match.SeriesName);
    }

    [Fact]
    public async Task FindMatchAsync_AuthorWithMultipleSeries_TitleMatch_ReturnsSpecificSeries()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            db.Series.Add(new Series { Name = "Discworld", Author = "Terry Pratchett", Type = SeriesType.Collection });
            db.Series.Add(new Series { Name = "Long Earth", Author = "Terry Pratchett", Type = SeriesType.Series });
            await db.SaveChangesAsync();
        }

        var service = new SeriesMatchService(factory);
        var match = await service.FindMatchAsync("The Long Earth", "Terry Pratchett");

        Assert.NotNull(match);
        Assert.Equal(MatchReason.TitleAndAuthorMatch, match.Reason);
        Assert.Equal("Long Earth", match.SeriesName);
    }

    [Fact]
    public async Task FindMatchAsync_AuthorWithMultipleUngroupedBooks_SuggestsCollection()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var someAuthor = new Author { Name = "Some Author" };
            db.Books.AddRange(
                new Book { Title = "Book A", Works = [new Work { Title = "Book A", Author = someAuthor }] },
                new Book { Title = "Book B", Works = [new Work { Title = "Book B", Author = someAuthor }] }
            );
            await db.SaveChangesAsync();
        }

        var service = new SeriesMatchService(factory);
        var match = await service.FindMatchAsync("Book C", "Some Author");

        Assert.NotNull(match);
        Assert.Equal(MatchReason.AuthorHasMultipleBooks, match.Reason);
    }

    [Fact]
    public async Task FindMatchAsync_NoMatch_ReturnsNull()
    {
        var factory = new TestDbContextFactory();
        var service = new SeriesMatchService(factory);

        var match = await service.FindMatchAsync("Random Book", "Unknown Author");

        Assert.Null(match);
    }

    [Fact]
    public async Task FindMatchAsync_NullInputs_ReturnsNull()
    {
        var factory = new TestDbContextFactory();
        var service = new SeriesMatchService(factory);

        Assert.Null(await service.FindMatchAsync(null, "Author"));
        Assert.Null(await service.FindMatchAsync("Title", null));
        Assert.Null(await service.FindMatchAsync(null, null));
    }

    [Theory]
    [InlineData("The Hobbit: Book 3", 3)]
    [InlineData("Ender's Game #2", 2)]
    [InlineData("No. 5 in the series", 5)]
    [InlineData("Vol. 1", 1)]
    [InlineData("Volume 3: The Return", 3)]
    [InlineData("Part II", 2)]
    [InlineData("Part III of the Saga", 3)]
    [InlineData("Just a Normal Title", null)]
    public void ExtractSeriesNumber_ParsesCorrectly(string title, int? expected)
    {
        Assert.Equal(expected, SeriesMatchService.ExtractSeriesNumber(title));
    }
}
