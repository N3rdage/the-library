using BookTracker.Application;
using BookTracker.Application.Series;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using NSubstitute;

namespace BookTracker.Tests.Services;

// The suggestion rules, over a substituted dispatcher. The reads behind them
// (name match, the author's series, the unseriesed count) are covered against
// SQL by GetSeriesMatchCandidatesHandlerTests.
[Trait("Category", TestCategories.Unit)]
public class SeriesMatchServiceTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private SeriesMatchService CreateService() => new(_dispatcher);

    private void GivenCandidates(
        SeriesCandidate? nameMatch = null, SeriesCandidate[]? authorSeries = null, int unseriesedCount = 0) =>
        _dispatcher.Query(Arg.Any<GetSeriesMatchCandidates>(), Arg.Any<CancellationToken>())
            .Returns(new SeriesMatchCandidates(nameMatch, authorSeries ?? [], unseriesedCount));

    private GetSeriesMatchCandidates? SentQuery() =>
        _dispatcher.ReceivedCalls()
            .Select(c => c.GetArguments()[0])
            .OfType<GetSeriesMatchCandidates>()
            .SingleOrDefault();

    private static BookLookupResult Lookup(string title, string author, string? series, int? number, string? raw) =>
        new(Isbn: "9780000000000", Title: title, Subtitle: null,
            Author: author, Publisher: null,
            GenreCandidates: [], DatePrinted: null, CoverUrl: null,
            Source: "Open Library",
            Series: series, SeriesNumber: number, SeriesNumberRaw: raw);

    [Fact]
    public async Task FindMatchAsync_AuthorWithExistingSeries_ReturnsSuggestion()
    {
        GivenCandidates(authorSeries: [new SeriesCandidate(1, "Discworld", SeriesType.Collection)]);

        var match = await CreateService().FindMatchAsync("The Colour of Magic", " Terry Pratchett ");

        Assert.NotNull(match);
        Assert.Equal(MatchReason.AuthorMatch, match.Reason);
        Assert.Equal("Discworld", match.SeriesName);
        Assert.Equal(1, match.SeriesId);
        Assert.Equal(new GetSeriesMatchCandidates(null, "Terry Pratchett"), SentQuery()); // trimmed, no name lookup
    }

    [Fact]
    public async Task FindMatchAsync_AuthorWithMultipleSeries_TitleMatch_ReturnsSpecificSeries()
    {
        GivenCandidates(authorSeries:
        [
            new SeriesCandidate(1, "Discworld", SeriesType.Collection),
            new SeriesCandidate(2, "Long Earth", SeriesType.Series),
        ]);

        var match = await CreateService().FindMatchAsync("The Long Earth", "Terry Pratchett");

        Assert.NotNull(match);
        Assert.Equal(MatchReason.TitleAndAuthorMatch, match.Reason);
        Assert.Equal("Long Earth", match.SeriesName);
    }

    [Fact]
    public async Task FindMatchAsync_AuthorWithMultipleSeries_NoTitleMatch_SuggestsFirst()
    {
        GivenCandidates(authorSeries:
        [
            new SeriesCandidate(1, "Discworld", SeriesType.Collection),
            new SeriesCandidate(2, "Long Earth", SeriesType.Series),
        ]);

        var match = await CreateService().FindMatchAsync("Good Omens", "Terry Pratchett");

        Assert.NotNull(match);
        Assert.Equal(MatchReason.AuthorMatch, match.Reason);
        Assert.Equal("Discworld", match.SeriesName);
        Assert.Contains("2 series/collections", match.Message);
    }

    [Fact]
    public async Task FindMatchAsync_AuthorWithMultipleUngroupedBooks_SuggestsCollection()
    {
        GivenCandidates(unseriesedCount: 2);

        var match = await CreateService().FindMatchAsync("Book C", "Some Author");

        Assert.NotNull(match);
        Assert.Equal(MatchReason.AuthorHasMultipleBooks, match.Reason);
        Assert.Contains("2 other books", match.Message);
    }

    [Fact]
    public async Task FindMatchAsync_AuthorWithOneUngroupedBook_DoesNotSuggestCollection()
    {
        GivenCandidates(unseriesedCount: 1);

        var match = await CreateService().FindMatchAsync("Book C", "Grouped Author");

        Assert.Null(match);
    }

    [Fact]
    public async Task FindMatchAsync_TitlePattern_SuggestsSeriesNumber()
    {
        GivenCandidates();

        var match = await CreateService().FindMatchAsync("Saga Vol. 3", "Unknown Author");

        Assert.NotNull(match);
        Assert.Equal(MatchReason.TitlePattern, match.Reason);
        Assert.Contains("#3", match.Message);
    }

    [Fact]
    public async Task FindMatchAsync_NoMatch_ReturnsNull()
    {
        GivenCandidates();

        var match = await CreateService().FindMatchAsync("Random Book", "Unknown Author");

        Assert.Null(match);
    }

    [Fact]
    public async Task FindMatchAsync_NullInputs_ReturnsNull_WithoutQuerying()
    {
        var service = CreateService();

        Assert.Null(await service.FindMatchAsync(null, "Author"));
        Assert.Null(await service.FindMatchAsync("Title", null));
        Assert.Null(await service.FindMatchAsync(null, null));
        Assert.Empty(_dispatcher.ReceivedCalls());
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
        GivenCandidates(nameMatch: new SeriesCandidate(7, "Discworld", SeriesType.Collection));

        var match = await CreateService().FindMatchAsync(
            Lookup("Sourcery", "Terry Pratchett", " discworld ", 5, "5"));

        Assert.NotNull(match);
        Assert.Equal(MatchReason.ApiMatchExisting, match.Reason);
        Assert.Equal("Discworld", match.SeriesName); // the local series' own casing
        Assert.Equal(7, match.SeriesId);
        Assert.Contains("Open Library", match.Message);
        Assert.Contains("#5", match.Message);
        Assert.Equal(new GetSeriesMatchCandidates("discworld", null), SentQuery()); // trimmed, name only
    }

    [Fact]
    public async Task FindMatchAsync_FromLookup_ApiSeriesNoLocalMatch_ReturnsApiMatchNewSeries()
    {
        GivenCandidates();

        var match = await CreateService().FindMatchAsync(
            Lookup("The Way of Kings", "Brandon Sanderson", "The Stormlight Archive", 1, "1"));

        Assert.NotNull(match);
        Assert.Equal(MatchReason.ApiMatchNewSeries, match.Reason);
        Assert.Equal("The Stormlight Archive", match.SeriesName);
        Assert.Null(match.SeriesId); // No local row yet — UI should propose creating one.
        Assert.Contains("accept to create the series", match.Message);
        Assert.Equal(1, match.SuggestedOrder); // Plumbed through for save-time order.
    }

    [Fact]
    public async Task FindMatchAsync_FromLookup_NonIntegerOrder_FloorsForSortAndKeepsRawForDisplay()
    {
        // Non-integer interquel orders ("2.5", Edgedancer) floor to a sort int
        // (2) so the book sorts next to its neighbours, and carry the raw string
        // through SuggestedOrderDisplay on Accept. The message shows the raw label.
        GivenCandidates();

        var match = await CreateService().FindMatchAsync(
            Lookup("Edgedancer", "Brandon Sanderson", "The Stormlight Archive", null, "2.5"));

        Assert.NotNull(match);
        Assert.Equal(2, match.SuggestedOrder);            // floored for sort + gaps
        Assert.Equal("2.5", match.SuggestedOrderDisplay); // raw label for display
        Assert.Contains("#2.5", match.Message);
    }

    [Fact]
    public async Task FindMatchAsync_FromLookup_NoApiSeries_FallsBackToLocalMatching()
    {
        GivenCandidates(authorSeries: [new SeriesCandidate(1, "Discworld", SeriesType.Collection)]);

        var match = await CreateService().FindMatchAsync(
            Lookup("Mort", "Terry Pratchett", null, null, null));

        // Falls back to FindMatchAsync(title, author), which finds the existing
        // Discworld series via author match.
        Assert.NotNull(match);
        Assert.Equal(MatchReason.AuthorMatch, match.Reason);
        Assert.Equal("Discworld", match.SeriesName);
        Assert.Equal(new GetSeriesMatchCandidates(null, "Terry Pratchett"), SentQuery());
    }
}
