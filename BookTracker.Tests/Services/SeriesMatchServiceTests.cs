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
                new Book { Title = "Book A", Works = [new Work { Title = "Book A", WorkAuthors = [new WorkAuthor { Author = someAuthor, Order = 0 }] }] },
                new Book { Title = "Book B", Works = [new Work { Title = "Book B", WorkAuthors = [new WorkAuthor { Author = someAuthor, Order = 0 }] }] }
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

    [Fact]
    public async Task FindMatchAsync_FromLookup_ApiSeriesMatchesLocal_ReturnsApiMatchExisting()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            db.Series.Add(new Series { Name = "Discworld", Author = "Terry Pratchett", Type = SeriesType.Collection });
            await db.SaveChangesAsync();
        }

        var service = new SeriesMatchService(factory);
        var lookup = new BookLookupResult(
            Isbn: "9780552134613", Title: "Sourcery", Subtitle: null,
            Author: "Terry Pratchett", Publisher: null,
            GenreCandidates: [], DatePrinted: null, CoverUrl: null,
            Source: "Open Library",
            Series: "Discworld", SeriesNumber: 5, SeriesNumberRaw: "5");

        var match = await service.FindMatchAsync(lookup);

        Assert.NotNull(match);
        Assert.Equal(MatchReason.ApiMatchExisting, match.Reason);
        Assert.Equal("Discworld", match.SeriesName);
        Assert.NotNull(match.SeriesId); // Should point to the existing local series.
        Assert.Contains("Open Library", match.Message);
        Assert.Contains("#5", match.Message);
    }

    [Fact]
    public async Task FindMatchAsync_FromLookup_ApiSeriesNoLocalMatch_ReturnsApiMatchNewSeries()
    {
        // No local Series rows seeded — the API series is a new one to us.
        var factory = new TestDbContextFactory();
        var service = new SeriesMatchService(factory);

        var lookup = new BookLookupResult(
            Isbn: "9780765326355", Title: "The Way of Kings", Subtitle: null,
            Author: "Brandon Sanderson", Publisher: null,
            GenreCandidates: [], DatePrinted: null, CoverUrl: null,
            Source: "Open Library",
            Series: "The Stormlight Archive", SeriesNumber: 1, SeriesNumberRaw: "1");

        var match = await service.FindMatchAsync(lookup);

        Assert.NotNull(match);
        Assert.Equal(MatchReason.ApiMatchNewSeries, match.Reason);
        Assert.Equal("The Stormlight Archive", match.SeriesName);
        Assert.Null(match.SeriesId); // No local row yet — UI should propose creating one.
        Assert.Contains("accept to create the series", match.Message);
        Assert.Equal(1, match.SuggestedOrder); // Plumbed through for save-time order.
    }

    [Fact]
    public async Task FindMatchAsync_FromLookup_NonIntegerOrder_SurfacesRawValueInMessage()
    {
        // Per revised Q3 default: non-integer orders leave SeriesNumber null
        // but the raw publisher string surfaces in the suggestion message so
        // the user can set Work.SeriesOrder manually if they want a position.
        var factory = new TestDbContextFactory();
        var service = new SeriesMatchService(factory);

        var lookup = new BookLookupResult(
            Isbn: "9780765326362", Title: "Edgedancer", Subtitle: null,
            Author: "Brandon Sanderson", Publisher: null,
            GenreCandidates: [], DatePrinted: null, CoverUrl: null,
            Source: "Open Library",
            Series: "The Stormlight Archive", SeriesNumber: null, SeriesNumberRaw: "2.5");

        var match = await service.FindMatchAsync(lookup);

        Assert.NotNull(match);
        Assert.Contains("'2.5'", match.Message);
        Assert.Contains("left blank", match.Message);
    }

    [Fact]
    public async Task FindMatchAsync_FromLookup_NoApiSeries_FallsBackToLocalMatching()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            db.Series.Add(new Series { Name = "Discworld", Author = "Terry Pratchett", Type = SeriesType.Collection });
            await db.SaveChangesAsync();
        }

        var service = new SeriesMatchService(factory);
        var lookup = new BookLookupResult(
            Isbn: "9780552134613", Title: "Mort", Subtitle: null,
            Author: "Terry Pratchett", Publisher: null,
            GenreCandidates: [], DatePrinted: null, CoverUrl: null,
            Source: "Open Library",
            Series: null, SeriesNumber: null, SeriesNumberRaw: null);

        var match = await service.FindMatchAsync(lookup);

        // Falls back to FindMatchAsync(title, author) which finds the existing
        // Discworld series via author match.
        Assert.NotNull(match);
        Assert.Equal(MatchReason.AuthorMatch, match.Reason);
        Assert.Equal("Discworld", match.SeriesName);
    }
}
