using BookTracker.Application;
using BookTracker.Application.Works;
using BookTracker.Shared.Online;
using BookTracker.Web.Api;
using BookTracker.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;

namespace BookTracker.Tests.Api;

// The response shapes of GET /api/online/isbn/{isbn}. The owned-work query
// itself is covered against the Testcontainer by FindOwnedWorkTests.
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
                "https://covers.example.test/colour.jpg", 1985, "Open Library", null),
            ok.Value);
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
}
