using BookTracker.Application;
using BookTracker.Application.Ai;
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

        Assert.Equal("Series name is required.", vm.Error);
    }

    [Fact]
    public async Task CreateCollectionFromSuggestion_success_clearsAPreviousError()
    {
        _dispatcher.Send(Arg.Any<CreateSeriesFromSuggestion>(), Arg.Any<CancellationToken>())
            .Returns<int?>(_ => throw new DomainRuleException("boom"), _ => 42);
        var vm = CreateVm();

        await vm.CreateCollectionFromSuggestionAsync(new CollectionGrouping("  ", "Series", "r", ["A"]));
        await vm.CreateCollectionFromSuggestionAsync(new CollectionGrouping("Fine", "Series", "r", ["A"]));

        Assert.Null(vm.Error);
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

    // --- Usage summary ---------------------------------------------------------

    private static AiUsageSummary Summary(params AiUsageLine[] thisMonth) => new(thisMonth, []);

    [Fact]
    public async Task LoadUsageSummary_queriesWithTheConfiguredPrices_andTotalsAcrossProviders()
    {
        var options = new AIOptions { Prices = { ["gpt-4o"] = new AiModelPrice(2.50m, 10.00m) } };
        _dispatcher.Query(Arg.Any<GetAiUsageSummary>(), Arg.Any<CancellationToken>()).Returns(Summary(
            new AiUsageLine("Anthropic", "claude-sonnet-4-6", 10, 80_000, 5_000, 0.30m),
            new AiUsageLine("AzureOpenAI", "gpt-4o", 2, 4_210, 1_120, 0.05m)));

        var vm = CreateVm(options);
        await vm.LoadUsageSummaryAsync();

        await _dispatcher.Received(1).Query(
            Arg.Is<GetAiUsageSummary>(q => ReferenceEquals(q!.Prices, options.Prices)), Arg.Any<CancellationToken>());
        Assert.Equal("This month: 12 calls, 84,210 in / 6,120 out tokens, about $0.35", vm.UsageLine);
    }

    [Fact]
    public async Task UsageLine_countsTheModelsWithoutAPrice()
    {
        _dispatcher.Query(Arg.Any<GetAiUsageSummary>(), Arg.Any<CancellationToken>()).Returns(Summary(
            new AiUsageLine("Anthropic", "claude-sonnet-4-6", 1, 100, 10, 0.0005m),
            new AiUsageLine("Anthropic", "claude-opus-4-6", 1, 100, 10, null),
            new AiUsageLine("AzureOpenAI", "gpt-4o", 1, 100, 10, null)));

        var vm = CreateVm();
        await vm.LoadUsageSummaryAsync();

        Assert.Equal("This month: 3 calls, 300 in / 30 out tokens, about $0.00 (2 models without a price in AI:Prices)", vm.UsageLine);
    }

    [Fact]
    public async Task UsageLine_withNoPricedModel_omitsTheCost()
    {
        _dispatcher.Query(Arg.Any<GetAiUsageSummary>(), Arg.Any<CancellationToken>()).Returns(Summary(
            new AiUsageLine("Anthropic", "claude-opus-4-6", 1, 100, 10, null)));

        var vm = CreateVm();
        await vm.LoadUsageSummaryAsync();

        Assert.Equal("This month: 1 call, 100 in / 10 out tokens (1 model without a price in AI:Prices)", vm.UsageLine);
    }

    [Fact]
    public async Task UsageLine_isNullUntilLoaded_andZeroCallsReadAsZero()
    {
        _dispatcher.Query(Arg.Any<GetAiUsageSummary>(), Arg.Any<CancellationToken>()).Returns(Summary());
        var vm = CreateVm();

        Assert.Null(vm.UsageLine);
        await vm.LoadUsageSummaryAsync();
        Assert.Equal("This month: 0 calls, 0 in / 0 out tokens", vm.UsageLine);
    }

    [Fact]
    public async Task AssessBook_refreshesTheUsageSummary_evenWhenTheCallFails()
    {
        // The provider's library read throws before any model is called, so the
        // AI call fails inside the VM's try/catch; the refresh after it must
        // still run.
        _dispatcher.Query(Arg.Any<GetLibraryProfileForAdvisor>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<AdvisorBook>>(_ => throw new InvalidOperationException("library unavailable"));
        _dispatcher.Query(Arg.Any<GetAiUsageSummary>(), Arg.Any<CancellationToken>()).Returns(Summary());
        var vm = CreateVm();
        vm.BookAdvisorQuery = "Dune";

        await vm.AssessBookAsync();

        Assert.NotNull(vm.AdvisorError);
        await _dispatcher.Received(1).Query(Arg.Any<GetAiUsageSummary>(), Arg.Any<CancellationToken>());
    }
}
