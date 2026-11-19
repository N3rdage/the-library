using BookTracker.Application.Series;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the series-suggestion reads (moved out of Web's
// SeriesMatchService, #265): the case-insensitive name match, the author's
// series, and the author's unseriesed book count.
[Trait("Category", TestCategories.Integration)]
public class GetSeriesMatchCandidatesHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<SeriesMatchCandidates> Load(string? seriesName, string? author) =>
        new GetSeriesMatchCandidatesHandler(_factory).HandleAsync(new GetSeriesMatchCandidates(seriesName, author));

    [Fact]
    public async Task NameMatch_IsCaseInsensitive_AndTrimmed()
    {
        int id;
        using (var db = _factory.CreateDbContext())
        {
            var s = new Series { Name = "Discworld", Author = "Terry Pratchett", Type = SeriesType.Collection };
            db.Series.Add(s);
            await db.SaveChangesAsync();
            id = s.Id;
        }

        var result = await Load("  DISCWORLD ", author: null);

        Assert.Equal(new SeriesCandidate(id, "Discworld", SeriesType.Collection), result.NameMatch);
        Assert.Empty(result.AuthorSeries);
        Assert.Equal(0, result.UnseriesedAuthorBookCount);
    }

    [Fact]
    public async Task NameMatch_NoLocalSeries_IsNull()
    {
        var result = await Load("The Stormlight Archive", author: null);

        Assert.Null(result.NameMatch);
    }

    [Fact]
    public async Task AuthorSeries_ReturnsEverySeriesWithThatAuthor()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Series.Add(new Series { Name = "Discworld", Author = "Terry Pratchett", Type = SeriesType.Collection });
            db.Series.Add(new Series { Name = "Long Earth", Author = "Terry Pratchett", Type = SeriesType.Series });
            db.Series.Add(new Series { Name = "Foundation", Author = "Isaac Asimov", Type = SeriesType.Series });
            db.Series.Add(new Series { Name = "No Author", Type = SeriesType.Series });
            await db.SaveChangesAsync();
        }

        var result = await Load(seriesName: null, " Terry Pratchett ");

        Assert.Null(result.NameMatch);
        Assert.Equal(["Discworld", "Long Earth"], result.AuthorSeries.Select(s => s.Name).Order());
        Assert.Equal(0, result.UnseriesedAuthorBookCount); // not counted when the author has a series
    }

    [Fact]
    public async Task UnseriesedCount_CountsTheAuthorsBooksWithNoSeries()
    {
        using (var db = _factory.CreateDbContext())
        {
            var author = Author.Create("Some Author");
            var series = new Series { Name = "Existing Series", Type = SeriesType.Series };
            db.Books.AddRange(
                TestBooks.Book("Book A", works: [TestWorks.Work("Book A", author)]),
                TestBooks.Book("Book B", works: [TestWorks.Work("Book B", author)]),
                TestBooks.Book("Book C", series: series, seriesOrder: 1, works: [TestWorks.Work("Book C", author)]));
            await db.SaveChangesAsync();
        }

        var result = await Load(seriesName: null, "Some Author");

        Assert.Empty(result.AuthorSeries);
        Assert.Equal(2, result.UnseriesedAuthorBookCount); // Book C is in a series
    }

    [Fact]
    public async Task UnseriesedCount_IsRoleBlind_CountingNonAuthorCredits()
    {
        // Pins the current behaviour: the count goes through the Work.Authors
        // skip navigation, which ignores the credit role, so a translator's
        // books count as the translator's own (reported on #265, not fixed here).
        using (var db = _factory.CreateDbContext())
        {
            var translator = Author.Create("Ann Translator");
            var lead = Author.Create("Lead Author");
            db.Books.Add(TestBooks.Book(
                "Translated",
                works:
                [
                    TestWorks.Work(
                        "Translated",
                        authorships:
                        [
                            TestWorks.Authorship(lead),
                            TestWorks.Authorship(translator, 0, AuthorRole.Translator),
                        ]),
                ]));
            await db.SaveChangesAsync();
        }

        var result = await Load(seriesName: null, "Ann Translator");

        Assert.Equal(1, result.UnseriesedAuthorBookCount);
    }

    [Fact]
    public async Task NoInputs_ReturnsEmpty()
    {
        var result = await Load(seriesName: null, author: null);

        Assert.Null(result.NameMatch);
        Assert.Empty(result.AuthorSeries);
        Assert.Equal(0, result.UnseriesedAuthorBookCount);
    }
}
