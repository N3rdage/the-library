using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Genres;
using BookTracker.Application.Series;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookTracker.Tests.ViewModels;

// Bulk add reads through FindEditionByIsbn, GetGenreOptions and (via
// SeriesMatchService) GetSeriesMatchCandidates, and writes through
// AddScannedCopy, CreateBook and EnsureSeries; a substituted dispatcher stands
// in for all of them. What those persist is covered by AddScannedCopyHandlerTests,
// CreateBookHandlerTests and FindEditionByIsbnHandlerTests. The AI provider is
// reached through AIProviderFactory.GetService(), substituted here so the
// "Ask AI for genres" path never leaves the process.
[Trait("Category", TestCategories.Unit)]
public class BulkAddViewModelTests
{
    private const int NewBookId = 99;

    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();
    private readonly IAIAssistantService _ai = Substitute.For<IAIAssistantService>();
    private readonly AIProviderFactory _providerFactory;

    // Options arrive name-ordered; the first match is taken in id order.
    private static readonly GenrePickerOption[] Taxonomy =
    [
        new(5, "Fantasy", null, null),
        new(1, "High Fantasy", 5, "Fantasy"),
        new(3, "Mystery", null, null),
        new(2, "Mystery", null, null),
    ];

    public BulkAddViewModelTests()
    {
        _dispatcher.Query(Arg.Any<GetSeriesMatchCandidates>(), Arg.Any<CancellationToken>())
            .Returns(new SeriesMatchCandidates(null, [], 0));
        _dispatcher.Query(Arg.Any<FindEditionByIsbn>(), Arg.Any<CancellationToken>())
            .Returns((ExistingBookMatch?)null);
        _dispatcher.Query(Arg.Any<GetGenreOptions>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GenrePickerOption>());
        // Non-null rather than Arg.Any: NSubstitute matches a Send<int> spec
        // against Send<int?> calls (int converts to int?).
        _dispatcher.Send(Arg.Is<CreateBook>(c => c != null), Arg.Any<CancellationToken>())
            .Returns(NewBookId);
        GivenScannedCopyResults((int?)null);

        _providerFactory = Substitute.For<AIProviderFactory>(_dispatcher, Options.Create(new AIOptions()), NullLoggerFactory.Instance);
        _providerFactory.GetService().Returns(_ai);
    }

    private BulkAddViewModel CreateVm() =>
        new(_lookup, new SeriesMatchService(_dispatcher), _providerFactory, NullLogger<BulkAddViewModel>.Instance, _dispatcher);

    private void GivenScannedCopyResults(int? first, params int?[] rest) =>
        _dispatcher.Send(Arg.Is<AddScannedCopy>(c => c != null), Arg.Any<CancellationToken>())
            .Returns(first, rest);

    private void GivenTaxonomy() =>
        _dispatcher.Query(Arg.Any<GetGenreOptions>(), Arg.Any<CancellationToken>()).Returns(Taxonomy);

    private void GivenLookupFinds(string isbn, string title, string author, params string[] genreCandidates) =>
        _lookup.LookupByIsbnAsync(isbn, Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(isbn, title, "There and Back Again", author, null, genreCandidates, null, null, "Open Library"));

    private void GivenAiSuggests(params string[] names) =>
        _ai.SuggestGenresAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new GenreSuggestionResult(names, "reasoning"));

    // LookupRowAsync is fire-and-forget from AddIsbnAsync and OnStateChanged is
    // its last step, so the callback doubles as the completion signal.
    private static async Task<BulkAddViewModel.DiscoveryRow> AddAndAwaitLookupAsync(BulkAddViewModel vm, string isbn)
    {
        var lookedUp = new TaskCompletionSource();
        vm.OnStateChanged = () =>
        {
            lookedUp.TrySetResult();
            return Task.CompletedTask;
        };
        vm.IsbnInput = isbn;
        await vm.AddIsbnAsync();
        await lookedUp.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return Assert.Single(vm.Rows);
    }

    private static (string Name, bool FromAi)[] NamesOf(BulkAddViewModel.DiscoveryRow row) =>
        row.Genres.Select(g => (g.Name, g.FromAi)).ToArray();

    private List<T> Sent<T>() =>
        _dispatcher.ReceivedCalls()
            .Select(c => c.GetArguments()[0])
            .OfType<T>()
            .ToList();

    private static BulkAddViewModel.DiscoveryRow FoundRow(string isbn, string title, string author) => new()
    {
        Isbn = isbn,
        Title = title,
        Author = author,
        Status = BulkAddViewModel.RowStatus.Found,
    };

    [Fact]
    public async Task AddIsbnAsync_AddsRowToGrid()
    {
        var vm = CreateVm();
        vm.IsbnInput = "9780345391803";
        vm.OnStateChanged = () => Task.CompletedTask;

        await vm.AddIsbnAsync();

        Assert.Single(vm.Rows);
        Assert.Equal("9780345391803", vm.Rows[0].Isbn);
        Assert.Equal("", vm.IsbnInput); // input is cleared
    }

    [Fact]
    public async Task AddIsbnAsync_AllowsSameIsbnAgain_ToCaptureSecondCopy()
    {
        // Re-scanning the same barcode in one session is meaningful — it's
        // a second physical copy of the book. Each scan creates its own
        // row; the save re-checks the library and turns the second save
        // into a Copy add rather than colliding on the unique ISBN constraint.
        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;

        vm.IsbnInput = "9780345391803";
        await vm.AddIsbnAsync();
        vm.IsbnInput = "9780345391803";
        await vm.AddIsbnAsync();

        Assert.Equal(2, vm.Rows.Count);
    }

    [Fact]
    public async Task AcceptRowAsync_SecondScanOfSameIsbn_AddsCopyInsteadOfDuplicateBook()
    {
        // The first save finds no edition and creates the book; by the second
        // save the edition exists, so AddScannedCopy adds the copy and no
        // second book is created.
        GivenScannedCopyResults(null, 7);
        var vm = CreateVm();
        var first = FoundRow("9780345391803", "The Hobbit", "J.R.R. Tolkien");
        var second = FoundRow("9780345391803", "The Hobbit", "J.R.R. Tolkien");

        await vm.AcceptRowAsync(first);
        await vm.AcceptRowAsync(second);

        var scans = Sent<AddScannedCopy>();
        Assert.Equal(2, scans.Count);
        Assert.All(scans, s => Assert.Equal("9780345391803", s.Isbn));
        Assert.All(scans, s => Assert.Empty(s.TagNames));
        Assert.Single(Sent<CreateBook>()); // Only one Book.
    }

    [Fact]
    public async Task FollowUpRowAsync_ExistingIsbn_AddsCopyWithFollowUpTag_InOneCommand()
    {
        GivenScannedCopyResults(7);
        var vm = CreateVm();
        var row = FoundRow("9780345391803", "The Hobbit", "J.R.R. Tolkien");
        vm.Rows.Add(row);

        await vm.FollowUpRowAsync(row);

        Assert.Empty(vm.Rows);
        Assert.Equal(["follow-up"], Assert.Single(Sent<AddScannedCopy>()).TagNames);
        Assert.Empty(Sent<CreateBook>());
    }

    [Fact]
    public async Task AddIsbnAsync_IgnoresEmptyInput()
    {
        var vm = CreateVm();
        vm.IsbnInput = "  ";

        await vm.AddIsbnAsync();

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task AddIsbnAsync_DetectsDuplicateInDatabase()
    {
        _dispatcher.Query(Arg.Is<FindEditionByIsbn>(q => q!.Isbn == "9780345391803"), Arg.Any<CancellationToken>())
            .Returns(new ExistingBookMatch(1, 2, "Existing", "Author", 1,
                null, "Author", null, null, null, DatePrecision.Day, BookFormat.TradePaperback));
        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;
        vm.IsbnInput = "9780345391803";

        await vm.AddIsbnAsync();

        Assert.True(vm.Rows[0].IsDuplicate);
    }

    [Fact]
    public async Task AddIsbnAsync_ExistingIsbn_HydratesFromLocalAndSkipsUpstreamLookup()
    {
        // Regression: bulk-add was calling LookupByIsbnAsync even for ISBNs
        // already in the library. When upstream providers don't index the
        // ISBN (e.g. older mass-market editions like 055210617X — Funny
        // Money by Richard Sapir, the trigger ISBN that surfaced this bug),
        // the row would render as "Unknown book" despite the book being on
        // a shelf. Local hydration must short-circuit the upstream call.
        _dispatcher.Query(Arg.Is<FindEditionByIsbn>(q => q!.Isbn == "055210617X"), Arg.Any<CancellationToken>())
            .Returns(new ExistingBookMatch(
                BookId: 1, EditionId: 2, Title: "Funny Money", Authors: "Richard Sapir", CopyCount: 1,
                LeadWorkSubtitle: "The Destroyer #54", LeadWorkAuthorship: "Richard Sapir",
                CoverUrl: "https://example.test/funny-money.jpg", Publisher: "Pinnacle",
                DatePrinted: new DateOnly(1983, 1, 1), DatePrintedPrecision: DatePrecision.Year,
                Format: BookFormat.MassMarketPaperback));

        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;
        vm.IsbnInput = "055210617X";

        await vm.AddIsbnAsync();

        var row = Assert.Single(vm.Rows);
        Assert.True(row.IsDuplicate);
        Assert.Equal(BulkAddViewModel.RowStatus.Found, row.Status);
        Assert.Equal("Funny Money", row.Title);
        Assert.Equal("The Destroyer #54", row.Subtitle);
        Assert.Equal("Richard Sapir", row.Author);
        Assert.Equal("Pinnacle", row.Publisher);
        Assert.Equal(BookFormat.MassMarketPaperback, row.Format);
        Assert.Equal(new DateOnly(1983, 1, 1), row.DatePrinted);
        Assert.Equal(DatePrecision.Year, row.DatePrintedPrecision);
        Assert.Equal("Local library", row.Source);
        Assert.Equal("https://example.test/funny-money.jpg", row.CoverUrl);

        await _lookup.DidNotReceive().LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RemoveRow_RemovesFromGrid()
    {
        var vm = CreateVm();
        var row = new BulkAddViewModel.DiscoveryRow { Isbn = "123" };
        vm.Rows.Add(row);

        vm.RemoveRow(row);

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task AcceptRowAsync_CreatesBookAndCopy_AndRemovesRowFromGrid()
    {
        var vm = CreateVm();
        var row = new BulkAddViewModel.DiscoveryRow
        {
            Isbn = "9780345391803",
            Title = "The Hobbit",
            Author = "J.R.R. Tolkien",
            CoverUrl = "https://example.test/hobbit.jpg",
            Publisher = "Ballantine",
            DatePrinted = new DateOnly(1982, 1, 1),
            DatePrintedPrecision = DatePrecision.Year,
            Status = BulkAddViewModel.RowStatus.Found,
        };
        vm.Rows.Add(row);

        await vm.AcceptRowAsync(row);

        Assert.Empty(vm.Rows); // row cleared post-accept so the discovery list stays manageable

        var sent = Assert.Single(Sent<CreateBook>());
        Assert.Equal(new NewBook("The Hobbit", BookCategory.Fiction, BookStatus.Unread, 0, null, "https://example.test/hobbit.jpg"), sent.Book);
        Assert.Equal(
            new NewEdition("9780345391803", BookFormat.TradePaperback, null, new DateOnly(1982, 1, 1), DatePrecision.Year, "Ballantine", null),
            sent.Edition);
        Assert.Equal(BookCondition.Good, sent.CopyCondition);
        Assert.Equal(new SeriesChoice(null, null, null), sent.Series);
        Assert.Empty(sent.TagNames);
    }

    [Fact]
    public async Task AcceptRowAsync_CreatesWorkAlongsideBook()
    {
        // The bulk-add flow saves a Book and a single Work in one go. The Work
        // carries the author and any genres derived from the lookup.
        var vm = CreateVm();
        var row = FoundRow("9780345391803", "The Hobbit", "J.R.R. Tolkien");
        row.Subtitle = "There and Back Again";

        await vm.AcceptRowAsync(row);

        var work = Assert.IsType<SingleWork>(Assert.Single(Sent<CreateBook>()).Works);
        Assert.Equal("The Hobbit", work.Title);
        Assert.Equal("There and Back Again", work.Subtitle);
        Assert.Equal(["J.R.R. Tolkien"], work.Authors);
        Assert.Empty(work.Contributors);
        Assert.Empty(work.GenreIds);
    }

    [Fact]
    public async Task AcceptRowAsync_SplitsAuthorCell_AndFallsBackToUnknown()
    {
        var vm = CreateVm();

        await vm.AcceptRowAsync(FoundRow("111", "Relic", "Douglas Preston, Lincoln Child"));
        await vm.AcceptRowAsync(FoundRow("222", "Nameless", "  "));

        var works = Sent<CreateBook>().Select(c => Assert.IsType<SingleWork>(c.Works)).ToList();
        Assert.Equal(["Douglas Preston", "Lincoln Child"], works[0].Authors);
        Assert.Equal(["Unknown"], works[1].Authors);
    }

    [Fact]
    public async Task LookupRow_FuzzyMatchesGenres_WithParents_InIdOrder_Once_AndTheSaveSendsThem()
    {
        GivenTaxonomy();
        GivenLookupFinds("111", "The Hobbit", "J.R.R. Tolkien",
            "High Fantasy", "Detective and mystery stories, English", "Fantasy fiction", "Cookery");
        var vm = CreateVm();

        var row = await AddAndAwaitLookupAsync(vm, "111");

        // "High Fantasy" (id 1) brings its parent Fantasy (5); the mystery
        // subject takes the lower-id "Mystery" (2); "Fantasy fiction" hits
        // Fantasy again and adds nothing; "Cookery" matches nothing. None of
        // it came from the AI, which the default-off switch never reached.
        Assert.Equal([("High Fantasy", false), ("Fantasy", false), ("Mystery", false)], NamesOf(row));
        await _ai.DidNotReceiveWithAnyArgs().SuggestGenresAsync(default!, default!, default, default!, default);

        await vm.AcceptRowAsync(row);

        var work = Assert.IsType<SingleWork>(Assert.Single(Sent<CreateBook>()).Works);
        Assert.Equal([1, 5, 2], work.GenreIds);
    }

    [Fact]
    public async Task LookupRow_SwitchOff_NeverAsksAi_EvenWhenNothingMatched()
    {
        GivenTaxonomy();
        GivenLookupFinds("111", "The Hobbit", "J.R.R. Tolkien", "Cookery");
        var vm = CreateVm();

        var row = await AddAndAwaitLookupAsync(vm, "111");

        Assert.False(vm.AskAiForGenres);
        Assert.Empty(row.Genres);
        Assert.Equal(BulkAddViewModel.RowStatus.Found, row.Status);
        await _ai.DidNotReceiveWithAnyArgs().SuggestGenresAsync(default!, default!, default, default!, default);
    }

    [Fact]
    public async Task LookupRow_SwitchOn_AsksAiWhenNothingMatched_AndMarksItsGenres()
    {
        GivenTaxonomy();
        GivenLookupFinds("111", "The Hobbit", "J.R.R. Tolkien", "Cookery");
        GivenAiSuggests("High Fantasy");
        var vm = CreateVm();
        vm.AskAiForGenres = true;

        var row = await AddAndAwaitLookupAsync(vm, "111");

        // The AI gets the row as the user sees it, with the (empty) rule-based
        // names; its child match brings the parent, both marked AI-sourced.
        await _ai.Received(1).SuggestGenresAsync(
            "The Hobbit", "J.R.R. Tolkien", "There and Back Again",
            Arg.Is<IReadOnlyList<string>>(g => g!.Count == 0), Arg.Any<CancellationToken>());
        Assert.Equal([("High Fantasy", true), ("Fantasy", true)], NamesOf(row));

        await vm.AcceptRowAsync(row);

        var work = Assert.IsType<SingleWork>(Assert.Single(Sent<CreateBook>()).Works);
        Assert.Equal([1, 5], work.GenreIds);
    }

    [Fact]
    public async Task LookupRow_SwitchOn_AsksAiWhenOnlyTopLevelMatched_AndAddsParentsOnce()
    {
        GivenTaxonomy();
        GivenLookupFinds("111", "The Hobbit", "J.R.R. Tolkien", "Fantasy fiction");
        GivenAiSuggests("High Fantasy", "Fantasy", "high fantasy");
        var vm = CreateVm();
        vm.AskAiForGenres = true;

        var row = await AddAndAwaitLookupAsync(vm, "111");

        // The rule-based Fantasy goes to the AI as a current genre and stays
        // rule-based; the AI's High Fantasy is added once and its parent is
        // already there, so nothing is duplicated.
        await _ai.Received(1).SuggestGenresAsync(
            "The Hobbit", "J.R.R. Tolkien", "There and Back Again",
            Arg.Is<IReadOnlyList<string>>(g => g!.SequenceEqual(new[] { "Fantasy" })), Arg.Any<CancellationToken>());
        Assert.Equal([("Fantasy", false), ("High Fantasy", true)], NamesOf(row));
    }

    [Fact]
    public async Task LookupRow_SwitchOn_SkipsAiWhenAChildGenreMatched()
    {
        GivenTaxonomy();
        GivenLookupFinds("111", "The Hobbit", "J.R.R. Tolkien", "High Fantasy");
        var vm = CreateVm();
        vm.AskAiForGenres = true;

        var row = await AddAndAwaitLookupAsync(vm, "111");

        Assert.Equal([("High Fantasy", false), ("Fantasy", false)], NamesOf(row));
        await _ai.DidNotReceiveWithAnyArgs().SuggestGenresAsync(default!, default!, default, default!, default);
    }

    [Fact]
    public async Task LookupRow_SwitchOn_DropsAiNamesOutsideTheTaxonomy()
    {
        GivenTaxonomy();
        GivenLookupFinds("111", "The Hobbit", "J.R.R. Tolkien");
        GivenAiSuggests("Cookery", "Mystery", "Fantasy Fiction");
        var vm = CreateVm();
        vm.AskAiForGenres = true;

        var row = await AddAndAwaitLookupAsync(vm, "111");

        // Only an exact taxonomy name counts (the lower-id Mystery); the AI's
        // free-form names are not fuzzy-matched the way subject strings are.
        Assert.Equal([("Mystery", true)], NamesOf(row));
        Assert.Equal([2], row.Genres.Select(g => g.Id));
    }

    [Fact]
    public async Task LookupRow_SwitchOn_ThrowingAi_LeavesRuleBasedGenres_AndTheRowCompletes()
    {
        GivenTaxonomy();
        GivenLookupFinds("111", "The Hobbit", "J.R.R. Tolkien", "Fantasy fiction");
        _ai.SuggestGenresAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("provider down"));
        var vm = CreateVm();
        vm.AskAiForGenres = true;

        var row = await AddAndAwaitLookupAsync(vm, "111");

        Assert.Equal(BulkAddViewModel.RowStatus.Found, row.Status);
        Assert.Equal("The Hobbit", row.Title);
        Assert.Equal([("Fantasy", false)], NamesOf(row));
    }

    [Fact]
    public async Task LookupRow_SwitchOn_StaysSearchingUntilTheAiAnswers_SoAcceptAllCannotSaveEmptyGenres()
    {
        // The grid offers Accept, and AcceptAllFoundAsync selects, on Found; a
        // row that turned Found before its genres were written could be saved
        // with none while the provider (about a second) was still answering.
        GivenTaxonomy();
        GivenLookupFinds("111", "The Hobbit", "J.R.R. Tolkien", "Cookery");
        var aiAnswer = new TaskCompletionSource<GenreSuggestionResult>();
        _ai.SuggestGenresAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(aiAnswer.Task);
        var vm = CreateVm();
        vm.AskAiForGenres = true;
        var lookedUp = new TaskCompletionSource();
        vm.OnStateChanged = () =>
        {
            lookedUp.TrySetResult();
            return Task.CompletedTask;
        };
        vm.IsbnInput = "111";

        // The lookup and taxonomy substitutes complete synchronously, so when
        // AddIsbnAsync returns the fire-and-forget lookup is parked on the AI.
        await vm.AddIsbnAsync();
        var row = Assert.Single(vm.Rows);

        await _ai.Received(1).SuggestGenresAsync("The Hobbit", "J.R.R. Tolkien", "There and Back Again", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        Assert.False(lookedUp.Task.IsCompleted);
        Assert.Equal(BulkAddViewModel.RowStatus.Searching, row.Status);
        Assert.Empty(row.Genres);

        await vm.AcceptAllFoundAsync();

        Assert.Contains(row, vm.Rows);
        Assert.Empty(Sent<CreateBook>());

        aiAnswer.SetResult(new GenreSuggestionResult(["High Fantasy"], "reasoning"));
        await lookedUp.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(BulkAddViewModel.RowStatus.Found, row.Status);
        Assert.Equal([("High Fantasy", true), ("Fantasy", true)], NamesOf(row));

        await vm.AcceptAllFoundAsync();

        Assert.Empty(vm.Rows);
        var work = Assert.IsType<SingleWork>(Assert.Single(Sent<CreateBook>()).Works);
        Assert.Equal([1, 5], work.GenreIds);
    }

    [Fact]
    public async Task LookupRow_SwitchOn_NoCandidatesAndNoTitle_QueriesNothing()
    {
        // Nothing to match and nothing to ask the AI about: the taxonomy query
        // is skipped too.
        GivenTaxonomy();
        GivenLookupFinds("111", "", "J.R.R. Tolkien");
        var vm = CreateVm();
        vm.AskAiForGenres = true;

        var row = await AddAndAwaitLookupAsync(vm, "111");

        Assert.Equal(BulkAddViewModel.RowStatus.Found, row.Status);
        Assert.Empty(row.Genres);
        await _dispatcher.DidNotReceive().Query(Arg.Any<GetGenreOptions>(), Arg.Any<CancellationToken>());
        await _ai.DidNotReceiveWithAnyArgs().SuggestGenresAsync(default!, default!, default, default!, default);
    }

    [Fact]
    public async Task FollowUpRowAsync_CreatesBookWithFollowUpTag_AndRemovesRowFromGrid()
    {
        var vm = CreateVm();
        var row = FoundRow("9780345391803", "The Hobbit", "J.R.R. Tolkien");
        vm.Rows.Add(row);

        await vm.FollowUpRowAsync(row);

        Assert.Empty(vm.Rows);
        var sent = Assert.Single(Sent<CreateBook>());
        Assert.Equal("The Hobbit", sent.Book.Title);
        Assert.Equal(["follow-up"], sent.TagNames);
    }

    [Fact]
    public async Task FollowUpNotFoundAsync_UsesDefaultTitle_AndRemovesRowFromGrid()
    {
        var vm = CreateVm();
        var row = new BulkAddViewModel.DiscoveryRow
        {
            Isbn = "1234567890",
            Status = BulkAddViewModel.RowStatus.NotFound
        };
        vm.Rows.Add(row);

        await vm.FollowUpNotFoundAsync(row);

        Assert.Empty(vm.Rows);
        Assert.StartsWith("Unknown book", row.Title); // title filled defensively before save
        var sent = Assert.Single(Sent<CreateBook>());
        Assert.Equal(row.Title, sent.Book.Title);
        Assert.Equal(["follow-up"], sent.TagNames);
    }

    [Fact]
    public async Task AcceptAllFoundAsync_AcceptsOnlyFoundNonDuplicates()
    {
        var vm = CreateVm();
        var found = new BulkAddViewModel.DiscoveryRow { Isbn = "111", Title = "A", Author = "A", Status = BulkAddViewModel.RowStatus.Found };
        var duplicate = new BulkAddViewModel.DiscoveryRow { Isbn = "222", Title = "B", Author = "B", Status = BulkAddViewModel.RowStatus.Found, IsDuplicate = true };
        var notFound = new BulkAddViewModel.DiscoveryRow { Isbn = "333", Status = BulkAddViewModel.RowStatus.NotFound };

        vm.Rows.AddRange([found, duplicate, notFound]);

        await vm.AcceptAllFoundAsync();

        Assert.DoesNotContain(found, vm.Rows);   // accepted, removed from grid
        Assert.Contains(duplicate, vm.Rows);     // skipped — duplicates need user review
        Assert.Contains(notFound, vm.Rows);      // skipped — needs follow-up decision
        Assert.Equal("A", Assert.Single(Sent<CreateBook>()).Book.Title);
    }

    [Theory]
    [InlineData(BulkAddViewModel.RowAction.Accepted, "table-success")]
    [InlineData(BulkAddViewModel.RowAction.FollowUp, "table-warning")]
    [InlineData(BulkAddViewModel.RowAction.Duplicate, "table-secondary")]
    [InlineData(BulkAddViewModel.RowAction.Pending, "")]
    public void RowCssClass_ReturnsCorrectClass(BulkAddViewModel.RowAction action, string expected)
    {
        var row = new BulkAddViewModel.DiscoveryRow { Action = action };
        Assert.Equal(expected, BulkAddViewModel.RowCssClass(row));
    }

    [Fact]
    public async Task AcceptRowAsync_WithAcceptedNewSeries_FindOrCreatesSeriesAndAttaches()
    {
        // Per-row analogue of the BookAddViewModel test: accepting an
        // ApiMatchNewSeries suggestion eager-creates the Series and the save
        // attaches the book to it by id.
        _dispatcher.Send(Arg.Is<EnsureSeries>(c => c!.Name == "The Stormlight Archive"), Arg.Any<CancellationToken>())
            .Returns((int?)12);
        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;

        // Construct the row directly to avoid the async lookup-pipeline
        // setup. AcceptSeriesSuggestion + the save don't depend on how
        // the row got its SeriesSuggestion populated.
        var row = FoundRow("9780765326355", "The Way of Kings", "Brandon Sanderson");
        row.SeriesSuggestion = new SeriesMatch(
            SeriesId: null,
            SeriesName: "The Stormlight Archive",
            SeriesType: null,
            Reason: MatchReason.ApiMatchNewSeries,
            Message: "Open Library suggests this is part of \"The Stormlight Archive\" #1 — accept to create the series and attach this book.",
            SuggestedOrder: 1);
        vm.Rows.Add(row);

        await vm.AcceptSeriesSuggestion(row);
        Assert.True(row.SeriesSuggestionAccepted);

        // Eager-create guard (TD-15a): Accept must find-or-create the Series row
        // and pin its id *now*, not defer to the save-time net. Without this the
        // save would still attach via SeriesResolver by name, leaving the eager
        // path false-green — a silent revert to save-time-only creation would go
        // uncaught.
        Assert.Equal(12, row.AcceptedSeriesId);
        Assert.Equal("The Stormlight Archive", Assert.Single(Sent<EnsureSeries>()).Name);

        await vm.AcceptRowAsync(row);

        Assert.Equal(new SeriesChoice(12, "The Stormlight Archive", "1"), Assert.Single(Sent<CreateBook>()).Series);
    }

    [Fact]
    public async Task AcceptRowAsync_WithAcceptedExistingSeries_AttachesBySeriesId()
    {
        const int seededSeriesId = 4;
        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;
        var row = FoundRow("9780552134613", "Sourcery", "Terry Pratchett");
        row.SeriesSuggestion = new SeriesMatch(
            SeriesId: seededSeriesId,
            SeriesName: "Discworld",
            SeriesType: SeriesType.Series,
            Reason: MatchReason.ApiMatchExisting,
            Message: "Open Library indicates this is part of \"Discworld\" #5",
            SuggestedOrder: 5);
        vm.Rows.Add(row);

        await vm.AcceptSeriesSuggestion(row);
        await vm.AcceptRowAsync(row);

        Assert.Equal(new SeriesChoice(seededSeriesId, "Discworld", "5"), Assert.Single(Sent<CreateBook>()).Series);
        // No new Series row created — attached to the existing one.
        Assert.Empty(Sent<EnsureSeries>());
    }

    [Fact]
    public async Task AcceptRowAsync_WithNonIntegerOrder_SendsTheDisplayLabel()
    {
        // The handler floors "2.5" into the sort key and keeps the display
        // (CreateBookHandlerTests.SeriesWithNonIntegerOrder_persistsFlooredOrderAndDisplay).
        const int seededSeriesId = 4;
        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;
        var row = FoundRow("9780765326362", "Edgedancer", "Brandon Sanderson");
        row.SeriesSuggestion = new SeriesMatch(
            SeriesId: seededSeriesId,
            SeriesName: "The Stormlight Archive",
            SeriesType: SeriesType.Series,
            Reason: MatchReason.ApiMatchExisting,
            Message: "Open Library indicates this is part of \"The Stormlight Archive\" #2.5",
            SuggestedOrder: 2,
            SuggestedOrderDisplay: "2.5");
        vm.Rows.Add(row);

        await vm.AcceptSeriesSuggestion(row);
        await vm.AcceptRowAsync(row);

        Assert.Equal(new SeriesChoice(seededSeriesId, "The Stormlight Archive", "2.5"), Assert.Single(Sent<CreateBook>()).Series);
    }

    [Fact]
    public async Task AcceptRowAsync_UnacceptedSuggestion_SendsNoSeries()
    {
        var vm = CreateVm();
        var row = FoundRow("9780552134613", "Sourcery", "Terry Pratchett");
        row.SeriesSuggestion = new SeriesMatch(4, "Discworld", SeriesType.Series, MatchReason.ApiMatchExisting, "msg", 5);

        await vm.AcceptRowAsync(row);

        Assert.Equal(new SeriesChoice(null, null, null), Assert.Single(Sent<CreateBook>()).Series);
    }

    private static BulkAddViewModel.DiscoveryRow FoundRow(string isbn, string title) => new()
    {
        Isbn = isbn,
        Title = title,
        Author = "Some Author",
        Status = BulkAddViewModel.RowStatus.Found,
    };

    [Fact]
    public async Task AcceptRowAsync_DomainRefusal_SetsErrorAndKeepsTheRow()
    {
        _dispatcher.Send(Arg.Is<CreateBook>(c => c != null), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("A title is required."));
        var vm = CreateVm();
        var row = FoundRow("111", "Some Book");
        vm.Rows.Add(row);

        var accepted = await vm.AcceptRowAsync(row);

        Assert.False(accepted);
        Assert.Equal("A title is required.", vm.Error);
        Assert.Same(row, Assert.Single(vm.Rows));
    }

    [Fact]
    public async Task AcceptRowAsync_Fault_PropagatesAndKeepsTheRow()
    {
        _dispatcher.Send(Arg.Is<CreateBook>(c => c != null), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db down"));
        var vm = CreateVm();
        var row = FoundRow("111", "Some Book");
        vm.Rows.Add(row);

        await Assert.ThrowsAsync<TimeoutException>(() => vm.AcceptRowAsync(row));

        Assert.Null(vm.Error);
        Assert.Single(vm.Rows);
    }

    [Fact]
    public async Task AcceptRowAsync_ScannedCopyNotFound_SetsErrorWithoutCreatingABook()
    {
        _dispatcher.Send(Arg.Is<AddScannedCopy>(c => c != null), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotFoundException("Edition not found."));
        var vm = CreateVm();
        var row = FoundRow("111", "Some Book");
        vm.Rows.Add(row);

        Assert.False(await vm.AcceptRowAsync(row));

        Assert.Equal("Edition not found.", vm.Error);
        Assert.Empty(Sent<CreateBook>());
        Assert.Single(vm.Rows);
    }

    [Fact]
    public async Task AcceptAllFoundAsync_StopsAtTheFirstRefusal()
    {
        _dispatcher.Send(Arg.Is<CreateBook>(c => c != null), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("A title is required."));
        var vm = CreateVm();
        vm.Rows.Add(FoundRow("111", "One"));
        vm.Rows.Add(FoundRow("222", "Two"));

        await vm.AcceptAllFoundAsync();

        Assert.Equal("A title is required.", vm.Error);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Single(Sent<CreateBook>());
    }
}
