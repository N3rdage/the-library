using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Series;
using BookTracker.Application.Wishlist;
using BookTracker.Application.Works;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

// The AI assistant's four data paths dispatch commands and queries only. The
// persistence each one used to do in the VM is covered by the handlers' own
// Integration tests (AIAssistantHandlerTests).
[Trait("Category", TestCategories.Unit)]
public class AIAssistantViewModelTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    // The paths under test never reach the AI service, so the provider factory
    // never dispatches a query.
    private AIAssistantViewModel CreateVm(AIOptions? options = null)
    {
        var aiOptions = Options.Create(options ?? new AIOptions());
        return new(_dispatcher, new AIProviderFactory(_dispatcher, aiOptions, NullLoggerFactory.Instance), aiOptions);
    }

    [Fact]
    public async Task LoadBooksNeedingGenres_copiesTheQueryRows()
    {
        List<BookGenreRow> rows = [new(1, "Alpha", null, "A", []), new(2, "Mort", "sub", "Terry Pratchett", ["Fantasy"])];
        _dispatcher.Query(Arg.Any<GetBooksNeedingGenres>(), Arg.Any<CancellationToken>()).Returns(rows);

        var vm = CreateVm();
        await vm.LoadBooksNeedingGenresAsync();

        Assert.True(vm.GenresLoaded);
        Assert.Same(rows, vm.BooksNeedingGenres);
    }

    [Fact]
    public async Task AcceptGenreSuggestions_sendsAddGenresToWork_andUpdatesTheRow()
    {
        _dispatcher.Query(Arg.Any<GetBooksNeedingGenres>(), Arg.Any<CancellationToken>())
            .Returns([new BookGenreRow(7, "Dune", null, "Frank Herbert", ["Adventure"])]);
        _dispatcher.Send(Arg.Any<AddGenresToWork>(), Arg.Any<CancellationToken>())
            .Returns(["Adventure", "Space Opera"]);

        var vm = CreateVm();
        await vm.LoadBooksNeedingGenresAsync();
        await vm.AcceptGenreSuggestionsAsync(7, ["Space Opera"]);

        await _dispatcher.Received(1).Send(
            Arg.Is<AddGenresToWork>(c => c!.BookId == 7 && c.GenreNames.SequenceEqual(new[] { "Space Opera" })),
            Arg.Any<CancellationToken>());
        Assert.Equal(new[] { "Adventure", "Space Opera" }, vm.BooksNeedingGenres.Single().CurrentGenres);
        Assert.Null(vm.CurrentSuggestion);
    }

    [Fact]
    public async Task AcceptGenreSuggestions_missingBook_leavesTheRowAlone()
    {
        _dispatcher.Query(Arg.Any<GetBooksNeedingGenres>(), Arg.Any<CancellationToken>())
            .Returns([new BookGenreRow(7, "Dune", null, "Frank Herbert", ["Adventure"])]);
        _dispatcher.Send(Arg.Any<AddGenresToWork>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>?)null);

        var vm = CreateVm();
        await vm.LoadBooksNeedingGenresAsync();
        await vm.AcceptGenreSuggestionsAsync(7, ["Space Opera"]);

        Assert.Equal(new[] { "Adventure" }, vm.BooksNeedingGenres.Single().CurrentGenres);
    }

    [Theory]
    [InlineData("Series", SeriesType.Series)]
    [InlineData("series", SeriesType.Series)]
    [InlineData("Collection", SeriesType.Collection)]
    [InlineData("Themed", SeriesType.Collection)]
    public async Task CreateCollectionFromSuggestion_sendsOneCommand_withTheMappedType(string type, SeriesType expected)
    {
        await CreateVm().CreateCollectionFromSuggestionAsync(
            new CollectionGrouping("Foundation", type, "Asimov's saga", ["Foundation", "Foundation and Empire"]));

        await _dispatcher.Received(1).Send(
            Arg.Is<CreateSeriesFromSuggestion>(c =>
                c!.Name == "Foundation" && c.Type == expected && c.Description == "Asimov's saga"
                && c.BookTitles.SequenceEqual(new[] { "Foundation", "Foundation and Empire" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateCollectionFromSuggestion_domainError_isSurfaced_notThrown()
    {
        _dispatcher.Send(Arg.Any<CreateSeriesFromSuggestion>(), Arg.Any<CancellationToken>())
            .Returns<int?>(_ => throw new DomainRuleException("Series name is required."));
        var vm = CreateVm();

        await vm.CreateCollectionFromSuggestionAsync(new CollectionGrouping("  ", "Series", "r", ["A"]));

        Assert.Equal("Series name is required.", vm.CreateCollectionError);
    }

    [Fact]
    public async Task CreateCollectionFromSuggestion_success_clearsAPreviousError()
    {
        _dispatcher.Send(Arg.Any<CreateSeriesFromSuggestion>(), Arg.Any<CancellationToken>())
            .Returns<int?>(_ => throw new DomainRuleException("boom"), _ => 42);
        var vm = CreateVm();

        await vm.CreateCollectionFromSuggestionAsync(new CollectionGrouping("  ", "Series", "r", ["A"]));
        await vm.CreateCollectionFromSuggestionAsync(new CollectionGrouping("Fine", "Series", "r", ["A"]));

        Assert.Null(vm.CreateCollectionError);
    }

    [Fact]
    public async Task AddRecommendationToWishlist_new_sendsAddWishlistItemAtMediumPriority()
    {
        _dispatcher.Query(Arg.Any<GetWishlistItemExists>(), Arg.Any<CancellationToken>()).Returns(false);

        await CreateVm().AddRecommendationToWishlistAsync(new BookRecommendation("Hyperion", "Dan Simmons", "because", null));

        await _dispatcher.Received(1).Query(
            Arg.Is<GetWishlistItemExists>(q => q!.Title == "Hyperion" && q.Author == "Dan Simmons"),
            Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).Send(
            Arg.Is<AddWishlistItem>(c =>
                c!.Title == "Hyperion" && c.Author == "Dan Simmons" && c.Priority == WishlistPriority.Medium
                && c.Isbns.Count == 0 && c.CoverUrl == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddRecommendationToWishlist_alreadyThere_sendsNothing()
    {
        _dispatcher.Query(Arg.Any<GetWishlistItemExists>(), Arg.Any<CancellationToken>()).Returns(true);

        await CreateVm().AddRecommendationToWishlistAsync(new BookRecommendation("Hyperion", "Dan Simmons", "because", null));

        await _dispatcher.DidNotReceive().Send(Arg.Any<AddWishlistItem>(), Arg.Any<CancellationToken>());
    }
}
