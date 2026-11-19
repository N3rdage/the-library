using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Works;
using BookTracker.Shared.Online;
using BookTracker.Web.Api;
using BookTracker.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;

namespace BookTracker.Tests.Api;

// The response shapes of the online endpoints. The Application queries
// behind them are covered against the Testcontainer by FindOwnedWorkTests,
// GetOwnedWorksByAuthorTests and FindOwnedEditionsTests.
[Trait("Category", TestCategories.Unit)]
public class OnlineEndpointsTests
{
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private static BookLookupResult Hit() => new(
        Isbn: "9780552124751",
        Title: "The Colour of Magic",
        Subtitle: "A Discworld Novel",
        Author: "Terry Pratchett",
        Publisher: "Corgi",
        GenreCandidates: [],
        DatePrinted: new DateOnly(1985, 6, 1),
        CoverUrl: "https://covers.example.test/colour.jpg",
        Source: "Open Library");

    private Task<IResult> HandleAsync(string isbn) =>
        OnlineEndpoints.HandleAsync(isbn, _lookup, _dispatcher, CancellationToken.None);

    [Theory]
    [InlineData("12345")]
    [InlineData("978-0-552-12475-1-9")]
    [InlineData("---")]
    public async Task WrongLengthIsbn_isABadRequest_andNoProviderIsCalled(string isbn)
    {
        var result = await HandleAsync(isbn);

        var bad = Assert.IsType<BadRequest<string>>(result);
        Assert.False(string.IsNullOrWhiteSpace(bad.Value));
        await _lookup.DidNotReceive().LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().Query(Arg.Any<FindOwnedWork>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoProviderAnswer_isNotFound()
    {
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BookLookupResult?)null);

        var result = await HandleAsync("9780552124751");

        Assert.IsType<NotFound>(result);
        await _dispatcher.DidNotReceive().Query(Arg.Any<FindOwnedWork>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProviderAnswerNotOwned_mapsTheFieldsWithNoOwnedMatch()
    {
        _lookup.LookupByIsbnAsync("9780552124751", Arg.Any<CancellationToken>()).Returns(Hit());
        _dispatcher.Query(Arg.Any<FindOwnedWork>(), Arg.Any<CancellationToken>()).Returns((OwnedWorkMatch?)null);

        var result = await HandleAsync("9780552124751");

        var ok = Assert.IsType<Ok<OnlineIsbnLookup>>(result);
        Assert.Equal(
            new OnlineIsbnLookup(
                "9780552124751", "The Colour of Magic", "A Discworld Novel", "Terry Pratchett", "Corgi",
                "https://covers.example.test/colour.jpg", 1985, "Open Library", null, null),
            ok.Value);
    }

    [Fact]
    public async Task ProviderWorkKey_isCarriedThrough()
    {
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Hit() with { WorkKey = "/works/OL45804W" });

        var result = await HandleAsync("9780552124751");

        var ok = Assert.IsType<Ok<OnlineIsbnLookup>>(result);
        Assert.Equal("/works/OL45804W", ok.Value!.WorkKey);
    }

    [Fact]
    public async Task ProviderAnswerOwned_carriesTheMatchThrough()
    {
        var owned = new OwnedWorkMatch(42, "The Colour of Magic", SameEdition: false);
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Hit());
        _dispatcher.Query(Arg.Any<FindOwnedWork>(), Arg.Any<CancellationToken>()).Returns(owned);

        var result = await HandleAsync("9780552124751");

        var ok = Assert.IsType<Ok<OnlineIsbnLookup>>(result);
        Assert.Equal(owned, ok.Value!.Owned);
    }

    [Fact]
    public async Task Isbn_isCleanedBeforeTheProviderAndTheOwnedQuery()
    {
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Hit() with { Isbn = "055212475X" });

        await HandleAsync("0-552-12475-x");

        await _lookup.Received(1).LookupByIsbnAsync("055212475X", Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).Query(
            new FindOwnedWork("055212475X", "The Colour of Magic", "Terry Pratchett"),
            Arg.Any<CancellationToken>());
    }

    // GET /api/online/author-works. The ownership query itself is covered
    // against the Testcontainer by GetOwnedWorksByAuthorTests.
    private Task<IResult> HandleAuthorWorksAsync(string? author) =>
        OnlineEndpoints.HandleAuthorWorksAsync(author, _lookup, _dispatcher, CancellationToken.None);

    private static OnlineAuthorWork Work(string key, string? title, int editions) =>
        new(key, title, 1990, $"https://covers.openlibrary.org/b/id/{editions}-M.jpg", editions, null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankAuthor_isABadRequest_andNothingIsCalled(string? author)
    {
        var result = await HandleAuthorWorksAsync(author);

        var bad = Assert.IsType<BadRequest<string>>(result);
        Assert.False(string.IsNullOrWhiteSpace(bad.Value));
        await _lookup.DidNotReceive().GetAuthorWorksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().Query(Arg.Any<GetOwnedWorksByAuthor>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProviderWorks_carryTheOwnedBookIdsTheQueryReturns()
    {
        _lookup.GetAuthorWorksAsync("Terry Pratchett", Arg.Any<CancellationToken>()).Returns(
        [
            Work("/works/OL1W", "The Colour of Magic", 90),
            Work("/works/OL2W", "Small Gods", 60),
            Work("/works/OL3W", null, 5),
        ]);
        _dispatcher.Query(Arg.Any<GetOwnedWorksByAuthor>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["The Colour of Magic"] = 42 });

        var result = await HandleAuthorWorksAsync("  Terry Pratchett ");

        var ok = Assert.IsType<Ok<OnlineAuthorWorks>>(result);
        Assert.Equal("Terry Pratchett", ok.Value!.Author);
        Assert.Equal(new int?[] { 42, null, null }, ok.Value.Works.Select(w => w.OwnedBookId));
        Assert.Equal(Work("/works/OL2W", "Small Gods", 60), ok.Value.Works[1]);
        await _dispatcher.Received(1).Query(
            Arg.Is<GetOwnedWorksByAuthor>(q =>
                q != null
                && q.Author == "Terry Pratchett"
                && q.Titles.SequenceEqual(new[] { "The Colour of Magic", "Small Gods" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyProviderList_isOkWithNoWorks_andNoLibraryQuery()
    {
        _lookup.GetAuthorWorksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await HandleAuthorWorksAsync("Nobody Atall");

        var ok = Assert.IsType<Ok<OnlineAuthorWorks>>(result);
        Assert.Equal("Nobody Atall", ok.Value!.Author);
        Assert.Empty(ok.Value.Works);
        await _dispatcher.DidNotReceive().Query(Arg.Any<GetOwnedWorksByAuthor>(), Arg.Any<CancellationToken>());
    }

    // GET /api/online/work-editions. The ownership query itself is covered
    // against the Testcontainer by FindOwnedEditionsTests.
    private Task<IResult> HandleWorkEditionsAsync(string? workKey) =>
        OnlineEndpoints.HandleWorkEditionsAsync(workKey, _lookup, _dispatcher, CancellationToken.None);

    private static OnlineEdition Edition(string? isbn, string? title = "Mort") =>
        new(isbn, title, "Corgi", "1988", "Paperback", null, null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OL45804W")]
    [InlineData("/works/OL45804M")]
    [InlineData("/books/OL45804W")]
    [InlineData("/works/OL45804W/editions")]
    [InlineData("/works/OLW")]
    public async Task MalformedWorkKey_isABadRequest_andNothingIsCalled(string? workKey)
    {
        var result = await HandleWorkEditionsAsync(workKey);

        var bad = Assert.IsType<BadRequest<string>>(result);
        Assert.False(string.IsNullOrWhiteSpace(bad.Value));
        await _lookup.DidNotReceive().GetWorkEditionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().Query(Arg.Any<FindOwnedEditions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProviderEditions_carryTheOwnedBookIdsTheQueryReturns()
    {
        _lookup.GetWorkEditionsAsync("/works/OL45804W", Arg.Any<CancellationToken>()).Returns(
        [
            Edition("9780552131063"),
            Edition("055213106X"),
            Edition(null, "Mort (book club)"),
            Edition("9780552131063"),
        ]);
        _dispatcher.Query(Arg.Any<FindOwnedEditions>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["9780552131063"] = 7 });

        var result = await HandleWorkEditionsAsync("/works/OL45804W");

        var ok = Assert.IsType<Ok<OnlineWorkEditions>>(result);
        Assert.Equal("/works/OL45804W", ok.Value!.WorkKey);
        Assert.Equal(new int?[] { 7, null, null, 7 }, ok.Value.Editions.Select(e => e.OwnedBookId));
        Assert.Equal(Edition("055213106X"), ok.Value.Editions[1]);
        await _dispatcher.Received(1).Query(
            Arg.Is<FindOwnedEditions>(q =>
                q != null && q.Isbns.SequenceEqual(new[] { "9780552131063", "055213106X" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditionsWithoutIsbns_areOk_andNoLibraryQuery()
    {
        _lookup.GetWorkEditionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([Edition(null)]);

        var result = await HandleWorkEditionsAsync("/works/OL45804W");

        var ok = Assert.IsType<Ok<OnlineWorkEditions>>(result);
        Assert.Equal(Edition(null), Assert.Single(ok.Value!.Editions));
        await _dispatcher.DidNotReceive().Query(Arg.Any<FindOwnedEditions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyProviderList_isOkWithNoEditions_andNoLibraryQuery()
    {
        _lookup.GetWorkEditionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await HandleWorkEditionsAsync("/works/OL1W");

        var ok = Assert.IsType<Ok<OnlineWorkEditions>>(result);
        Assert.Equal("/works/OL1W", ok.Value!.WorkKey);
        Assert.Empty(ok.Value.Editions);
        await _dispatcher.DidNotReceive().Query(Arg.Any<FindOwnedEditions>(), Arg.Any<CancellationToken>());
    }
}
