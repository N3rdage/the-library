using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Series;
using BookTracker.Application.Works;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookTracker.Tests.ViewModels;

// The Add page reads through GetSeriesOptions, FindEditionByIsbn and (via
// SeriesMatchService) GetSeriesMatchCandidates, and writes through CreateBook,
// AddCopyToEdition and EnsureSeries; a substituted dispatcher stands in for all
// of them. What CreateBook persists is covered by CreateBookHandlerTests, the
// existing-edition read by FindEditionByIsbnHandlerTests, and the series and
// copy commands by their own handler tests.
[Trait("Category", TestCategories.Unit)]
public class BookAddViewModelTests
{
    private const int NewBookId = 99;

    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();

    public BookAddViewModelTests()
    {
        _dispatcher.Query(Arg.Any<GetSeriesMatchCandidates>(), Arg.Any<CancellationToken>())
            .Returns(new SeriesMatchCandidates(null, [], 0));
        _dispatcher.Query(Arg.Any<FindEditionByIsbn>(), Arg.Any<CancellationToken>())
            .Returns((ExistingBookMatch?)null);
        _dispatcher.Query(Arg.Any<GetSeriesOptions>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SeriesPickerOption>());
        // Non-null rather than Arg.Any: NSubstitute matches a Send<int> spec
        // against Send<int?> calls (int converts to int?), and the null argument
        // of a later EnsureSeries setup call would otherwise hit this stub.
        _dispatcher.Send(Arg.Is<CreateBook>(c => c != null), Arg.Any<CancellationToken>())
            .Returns(NewBookId);
    }

    private BookAddViewModel CreateVm() =>
        new(_lookup, new SeriesMatchService(_dispatcher), _dispatcher, NullLogger<BookAddViewModel>.Instance);

    private static ExistingBookMatch HobbitMatch() =>
        new(BookId: 4, EditionId: 5, Title: "The Hobbit", Authors: "Tolkien", CopyCount: 1,
            LeadWorkSubtitle: null, LeadWorkAuthorship: "Tolkien", CoverUrl: null, Publisher: null,
            DatePrinted: null, DatePrintedPrecision: DatePrecision.Day, Format: BookFormat.TradePaperback);

    private void GivenSeriesOptions(params SeriesPickerOption[] options) =>
        _dispatcher.Query(Arg.Any<GetSeriesOptions>(), Arg.Any<CancellationToken>()).Returns(options);

    private void GivenEnsureSeries(string name, int id) =>
        _dispatcher.Send(Arg.Is<EnsureSeries>(c => c!.Name == name), Arg.Any<CancellationToken>())
            .Returns((int?)id);

    private void GivenLookup(string isbn, string title, string author, string? series, int? seriesNumber) =>
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: isbn, Title: title, Subtitle: null,
                Author: author, Publisher: null,
                GenreCandidates: [], DatePrinted: null, CoverUrl: null,
                Source: "Open Library",
                Series: series, SeriesNumber: seriesNumber, SeriesNumberRaw: seriesNumber?.ToString()));

    private CreateBook SentCreateBook() =>
        _dispatcher.ReceivedCalls()
            .Select(c => c.GetArguments()[0])
            .OfType<CreateBook>()
            .Single();

    private IEnumerable<EnsureSeries> SentEnsureSeries() =>
        _dispatcher.ReceivedCalls()
            .Select(c => c.GetArguments()[0])
            .OfType<EnsureSeries>();

    [Fact]
    public void AddCollectionWorkRow_StartsEmpty_RegardlessOfPreviousRowAuthors()
    {
        // Inheritance was killed 2026-05-24 — the SingleAuthor toggle is
        // now the explicit signal for "same author across all rows."
        // Without the toggle, the user has flagged "different authors per
        // row" and a new row inheriting the previous row's authors is
        // exactly the wrong default (forces remove-then-add on every row).
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.CollectionWorks[0].Authors = new List<string> { "Stephen King" };

        vm.AddCollectionWorkRow();

        Assert.Empty(vm.CollectionWorks[1].Authors);
    }

    [Fact]
    public void AddCollectionWorkRow_NoPopulatedRows_StartsEmpty()
    {
        // First time the + button is tapped, before any author has been
        // typed, the new row must start with an empty Authors list (not
        // null, not throw).
        var vm = CreateVm();
        vm.IsCollection = true;
        // Default 1 empty starter row.

        vm.AddCollectionWorkRow();

        Assert.Equal(2, vm.CollectionWorks.Count);
        Assert.Empty(vm.CollectionWorks[1].Authors);
    }

    [Fact]
    public void DefaultCollectionState_StartsWithOneEmptyRow()
    {
        // The capture flow grows the row list via Enter-on-Title; pre-
        // seeding multiple rows forced users to manually re-enter authors
        // on row 2 (the row already existed before any author was typed,
        // so the new-row inheritance didn't help). Verify the default
        // shape stays single-row.
        var vm = CreateVm();

        Assert.Single(vm.CollectionWorks);
        Assert.False(vm.SingleAuthor);
        Assert.Empty(vm.SharedAuthors);
    }

    [Fact]
    public void SingleAuthorToggle_On_SeedsSharedAuthorsFromUnionOfRows()
    {
        // OFF → ON: the user has been entering per-row authors, then
        // decides "actually let's set a default". Shared should capture
        // the union of what was already on the rows so toggling isn't
        // destructive.
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.CollectionWorks =
        [
            new() { Authors = ["Stephen King"] },
            new() { Authors = ["Stephen King", "Peter Straub"] },
            new() { Authors = [] },
        ];

        vm.SingleAuthor = true;

        Assert.Equal(new[] { "Stephen King", "Peter Straub" }, vm.SharedAuthors);
    }

    [Fact]
    public void SingleAuthorToggle_Off_BroadcastsSharedToEveryRow()
    {
        // ON → OFF: the user picked a default in shared mode and now wants
        // to tweak one row. Each row should start with the shared list as
        // its starting point so the user only edits the outlier rows.
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.CollectionWorks =
        [
            new() { Title = "A" },
            new() { Title = "B" },
            new() { Title = "C" },
        ];
        vm.SingleAuthor = true;
        vm.SharedAuthors = ["Stephen King"];

        vm.SingleAuthor = false;

        Assert.All(vm.CollectionWorks, w => Assert.Equal(new[] { "Stephen King" }, w.Authors));
    }

    [Fact]
    public void SingleAuthorToggle_Off_GivesEachRowAnIndependentList()
    {
        // The broadcast must copy the list, not share a reference — picker
        // mutations on one row would otherwise leak into every other row.
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.CollectionWorks = [new() { Title = "A" }, new() { Title = "B" }];
        vm.SingleAuthor = true;
        vm.SharedAuthors = ["Stephen King"];

        vm.SingleAuthor = false;

        vm.CollectionWorks[0].Authors.Add("Co-author");
        Assert.Single(vm.CollectionWorks[1].Authors);
        Assert.Equal("Stephen King", vm.CollectionWorks[1].Authors[0]);
    }

    [Fact]
    public void SingleGenreToggle_On_SeedsSharedGenresFromUnionOfRows()
    {
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.CollectionWorks =
        [
            new() { GenreIds = [1, 2] },
            new() { GenreIds = [2, 3] },
            new() { GenreIds = [] },
        ];

        vm.SingleGenre = true;

        Assert.Equal(new[] { 1, 2, 3 }, vm.SharedGenreIds);
    }

    [Fact]
    public void SingleGenreToggle_Off_BroadcastsSharedToEveryRow()
    {
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.CollectionWorks = [new() { Title = "A" }, new() { Title = "B" }, new() { Title = "C" }];
        vm.SingleGenre = true;
        vm.SharedGenreIds = [7];

        vm.SingleGenre = false;

        Assert.All(vm.CollectionWorks, w => Assert.Equal(new[] { 7 }, w.GenreIds));
    }

    [Fact]
    public void SingleGenreToggle_Off_GivesEachRowAnIndependentList()
    {
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.CollectionWorks = [new() { Title = "A" }, new() { Title = "B" }];
        vm.SingleGenre = true;
        vm.SharedGenreIds = [7];

        vm.SingleGenre = false;

        vm.CollectionWorks[0].GenreIds.Add(9);
        Assert.Single(vm.CollectionWorks[1].GenreIds);
        Assert.Equal(7, vm.CollectionWorks[1].GenreIds[0]);
    }

    [Fact]
    public void Reset_DoesNotBroadcastStaleSharedStateToFreshRow()
    {
        // Regression: Reset() flips SingleAuthor / SingleGenre back to false.
        // If those setters propagated, the freshly-rebuilt empty starter
        // row would be re-populated with whatever shared state the previous
        // capture had.
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.SingleAuthor = true;
        vm.SharedAuthors = ["Stephen King"];
        vm.SingleGenre = true;
        vm.SharedGenreIds = [7];

        vm.Reset();

        Assert.False(vm.SingleAuthor);
        Assert.False(vm.SingleGenre);
        Assert.Empty(vm.SharedAuthors);
        Assert.Empty(vm.SharedGenreIds);
        Assert.Single(vm.CollectionWorks);
        Assert.Empty(vm.CollectionWorks[0].Authors);
        Assert.Empty(vm.CollectionWorks[0].GenreIds);
    }

    [Fact]
    public async Task SaveAsync_SendsEditionNumber_AndReturnsTheNewBookId()
    {
        // Capture flow lock: typing "3" into the Edition # field on
        // EditionCopyForm must reach the command's edition. Backs the Joy of
        // Cooking 3rd ed. vs 9th ed. disambiguation that motivated the column.
        var vm = CreateVm();
        vm.BookInput.Title = "Joy of Cooking";
        vm.WorkInput.Title = "Joy of Cooking";
        vm.WorkInput.Authors = ["Irma Rombauer"];
        vm.EditionInput.Format = BookFormat.Hardcover;
        vm.EditionInput.EditionNumber = 3;

        var bookId = await vm.SaveAsync(selectedGenreIds: []);

        Assert.Equal(NewBookId, bookId);
        var edition = SentCreateBook().Edition;
        Assert.Equal(3, edition.EditionNumber);
        Assert.Equal(BookFormat.Hardcover, edition.Format);
        Assert.False(vm.Saving);
    }

    [Fact]
    public async Task SaveAsync_NullEditionNumber_SendsNull()
    {
        // The common case — fiction reprints, mass-market paperbacks
        // where the edition number isn't on the cover. Field stays blank.
        var vm = CreateVm();
        vm.BookInput.Title = "Foundation";
        vm.WorkInput.Title = "Foundation";
        vm.WorkInput.Authors = ["Isaac Asimov"];
        vm.EditionInput.Format = BookFormat.MassMarketPaperback;

        await vm.SaveAsync(selectedGenreIds: []);

        Assert.Null(SentCreateBook().Edition.EditionNumber);
    }

    [Fact]
    public async Task SaveAsync_SingleAuthorMode_SendsSharedAuthorsWithEveryRow()
    {
        // Single-Author mode is the "King's Different Seasons" shape — the
        // user enters the author once at the top; the handler applies it to
        // every Work row.
        var vm = CreateVm();
        vm.BookInput.Title = "Different Seasons";
        vm.EditionInput.Format = BookFormat.TradePaperback;
        vm.IsCollection = true;
        vm.SingleAuthor = true;
        vm.SharedAuthors = new List<string> { "Stephen King" };
        vm.CollectionWorks =
        [
            new() { Title = "Rita Hayworth and Shawshank Redemption" },
            new() { Title = "Apt Pupil" },
            new() { Title = "The Body" },
            new() { Title = "The Breathing Method" },
        ];

        await vm.SaveAsync(selectedGenreIds: []);

        var collection = Assert.IsType<WorkCollection>(SentCreateBook().Works);
        Assert.True(collection.SingleAuthor);
        Assert.Equal(["Stephen King"], collection.SharedAuthors);
        Assert.Equal(
            ["Rita Hayworth and Shawshank Redemption", "Apt Pupil", "The Body", "The Breathing Method"],
            collection.Rows.Select(r => r.Title));
    }

    [Fact]
    public async Task SaveAsync_Collection_SendsBookLevelSeries()
    {
        // Series membership is a per-Book concept (the book is installment N
        // of a publication series), so a collection can belong to a series
        // just like a single-work book; the collection save carries it too.
        GivenEnsureSeries("The Sandman", 11);
        var vm = CreateVm();
        vm.BookInput.Title = "The Sandman: Season of Mists";
        vm.EditionInput.Format = BookFormat.TradePaperback;
        vm.IsCollection = true;
        vm.SingleAuthor = true;
        vm.SharedAuthors = new List<string> { "Neil Gaiman" };
        vm.CollectionWorks =
        [
            new() { Title = "Chapter 1" },
            new() { Title = "Chapter 2" },
        ];
        await vm.OnSeriesChosenAsync("The Sandman");
        vm.AcceptedSeriesOrderLabel = "4";

        await vm.SaveAsync(selectedGenreIds: []);

        var command = SentCreateBook();
        Assert.IsType<WorkCollection>(command.Works);
        Assert.Equal(new SeriesChoice(11, "The Sandman", "4"), command.Series);
    }

    [Fact]
    public async Task SaveAsync_HandlerValidationError_PropagatesAndClearsSaving()
    {
        // The handler owns the user-facing validation throws (no contributor,
        // no usable collection row, Single-Author mode with no shared author).
        // The Add page shows an InvalidOperationException's message as-is, so
        // the VM must let it through unchanged.
        _dispatcher.Send(Arg.Any<CreateBook>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("A collection in Single-Author mode needs at least one work with a title, plus at least one shared author. (Or attach an existing work.)"));
        var vm = CreateVm();
        vm.BookInput.Title = "Untitled Collection";
        vm.IsCollection = true;
        vm.SingleAuthor = true;
        vm.SharedAuthors = [];
        vm.CollectionWorks = [new() { Title = "Story A" }];

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveAsync(selectedGenreIds: []));

        Assert.Contains("Single-Author", ex.Message);
        Assert.False(vm.Saving);
    }

    [Fact]
    public void AddCollectionWorkRow_StartsEmpty_RegardlessOfPreviousRowGenres()
    {
        // Mirror of the author inheritance kill — the SingleGenre toggle
        // is now the explicit signal for "same genre across all rows."
        // Without it, the user wants per-row genres, not inheritance.
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.CollectionWorks[0].GenreIds = new List<int> { 7 };

        vm.AddCollectionWorkRow();

        Assert.Empty(vm.CollectionWorks[1].GenreIds);
    }

    [Fact]
    public async Task SaveAsync_SingleGenreMode_SendsSharedGenreIds()
    {
        // Single-Genre mode is Drew's order-of-magnitude case — pick the
        // genre once at the top; the handler applies it to every Work row.
        var vm = CreateVm();
        vm.BookInput.Title = "The Mammoth Book of SF";
        vm.IsCollection = true;
        vm.SingleAuthor = true;
        vm.SharedAuthors = new List<string> { "Various" };
        vm.SingleGenre = true;
        vm.SharedGenreIds = new List<int> { 12 };
        vm.CollectionWorks =
        [
            new() { Title = "Story A" },
            new() { Title = "Story B" },
            new() { Title = "Story C" },
        ];

        await vm.SaveAsync(selectedGenreIds: []);

        var collection = Assert.IsType<WorkCollection>(SentCreateBook().Works);
        Assert.True(collection.SingleGenre);
        Assert.Equal([12], collection.SharedGenreIds);
        Assert.Equal(3, collection.Rows.Count);
    }

    [Fact]
    public async Task SaveAsync_PerRowGenres_SendsEachRowsOwnList()
    {
        // Single-Genre OFF: each row carries its own GenreIds.
        var vm = CreateVm();
        vm.BookInput.Title = "Mostly SF, One Horror";
        vm.IsCollection = true;
        vm.SingleGenre = false;
        vm.CollectionWorks =
        [
            new() { Title = "SF One", Authors = ["Author A"], GenreIds = [1] },
            new() { Title = "SF Two", Authors = ["Author A"], GenreIds = [1] },
            new() { Title = "Horror One", Authors = ["Author A"], GenreIds = [2] },
        ];

        await vm.SaveAsync(selectedGenreIds: []);

        var collection = Assert.IsType<WorkCollection>(SentCreateBook().Works);
        Assert.False(collection.SingleGenre);
        Assert.Equal(new[] { new[] { 1 }, new[] { 1 }, new[] { 2 } }, collection.Rows.Select(r => r.GenreIds.ToArray()));
        Assert.All(collection.Rows, r => Assert.Equal(["Author A"], r.Authors));
    }

    [Fact]
    public async Task SearchAsync_EmptyInputs_ReportsMessageAndDoesNotCallLookup()
    {
        var vm = CreateVm();
        vm.SearchTitle = "";
        vm.SearchAuthor = "  ";

        await vm.SearchAsync();

        Assert.NotNull(vm.SearchMessage);
        await _lookup.DidNotReceiveWithAnyArgs().SearchByTitleAuthorAsync(default, default, default);
    }

    [Fact]
    public async Task SearchAsync_PopulatesCandidates()
    {
        var vm = CreateVm();
        vm.SearchTitle = "Murder on the Orient Express";
        vm.SearchAuthor = "Agatha Christie";

        var candidate = new BookSearchCandidate(
            WorkKey: "/works/OL45804W",
            Title: "Murder on the Orient Express",
            Author: "Agatha Christie",
            FirstPublishYear: 1934,
            EditionCount: 245,
            CoverUrl: "https://covers.openlibrary.org/b/id/12345-M.jpg",
            OpenLibraryUrl: "https://openlibrary.org/works/OL45804W/editions");

        _lookup.SearchByTitleAuthorAsync("Murder on the Orient Express", "Agatha Christie", Arg.Any<CancellationToken>())
            .Returns([candidate]);

        await vm.SearchAsync();

        Assert.Single(vm.SearchCandidates);
        Assert.Equal("Murder on the Orient Express", vm.SearchCandidates[0].Title);
        Assert.Null(vm.SearchMessage);
    }

    [Fact]
    public async Task SearchAsync_NoResults_SetsHelpfulMessage()
    {
        var vm = CreateVm();
        vm.SearchTitle = "An obscure title that doesn't exist";

        _lookup.SearchByTitleAuthorAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BookSearchCandidate>());

        await vm.SearchAsync();

        Assert.Empty(vm.SearchCandidates);
        Assert.Contains("No matches", vm.SearchMessage);
    }

    [Fact]
    public async Task ApplyCandidateAsync_PrefillsEmptyFields()
    {
        var vm = CreateVm();
        var candidate = new BookSearchCandidate(
            WorkKey: "/works/OL1W",
            Title: "And Then There Were None",
            Author: "Agatha Christie",
            FirstPublishYear: 1939,
            EditionCount: 100,
            CoverUrl: "https://example.invalid/cover.jpg",
            OpenLibraryUrl: null);

        await vm.ApplyCandidateAsync(candidate);

        // Author and title flow into both BookInput (for the Book record) and
        // WorkInput (for the auto-created primary Work).
        Assert.Equal("And Then There Were None", vm.BookInput.Title);
        Assert.Equal("And Then There Were None", vm.WorkInput.Title);
        Assert.Equal(new[] { "Agatha Christie" }, vm.WorkInput.Authors);
        Assert.Equal("https://example.invalid/cover.jpg", vm.BookInput.DefaultCoverArtUrl);
        // Year-only candidate → year-precision text in the input.
        Assert.Equal("1939", vm.WorkInput.FirstPublishedDate);
    }

    [Fact]
    public async Task ApplyCandidateAsync_DoesNotOverwriteUserFilledFields()
    {
        var vm = CreateVm();
        vm.BookInput.Title = "User typed this";
        vm.WorkInput.Title = "User typed this";
        vm.WorkInput.Authors = ["User author"];
        vm.WorkInput.FirstPublishedDate = "15 Jun 1939";

        var candidate = new BookSearchCandidate(
            WorkKey: "/works/OL1W",
            Title: "Lookup title",
            Author: "Lookup author",
            FirstPublishYear: 1939,
            EditionCount: 1,
            CoverUrl: null,
            OpenLibraryUrl: null);

        await vm.ApplyCandidateAsync(candidate);

        Assert.Equal("User typed this", vm.BookInput.Title);
        Assert.Equal(new[] { "User author" }, vm.WorkInput.Authors);
        Assert.Equal("15 Jun 1939", vm.WorkInput.FirstPublishedDate);
    }

    [Fact]
    public async Task SaveAsync_SendsIsbnAsEntered()
    {
        // The VM passes the ISBN through untouched; the Edition aggregate
        // trims it (blank → null). Normalisation is not part of the save.
        var vm = CreateVm();
        vm.BookInput.Title = "And Then There Were None";
        vm.WorkInput.Title = "And Then There Were None";
        vm.WorkInput.Authors = ["Agatha Christie"];
        vm.EditionInput.Isbn = " 978-0-00-713683-4 ";

        await vm.SaveAsync(new List<int>());

        Assert.Equal(" 978-0-00-713683-4 ", SentCreateBook().Edition.Isbn);
    }

    [Fact]
    public async Task LookupAsync_ExistingIsbn_FlagsExistingBookInsteadOfPrefilling()
    {
        var match = HobbitMatch();
        _dispatcher.Query(Arg.Is<FindEditionByIsbn>(q => q!.Isbn == "9780345391803"), Arg.Any<CancellationToken>())
            .Returns(match);
        var vm = CreateVm();
        vm.LookupIsbn = "9780345391803";

        await vm.LookupAsync();

        Assert.Equal(match, vm.ExistingBook);
        Assert.Null(vm.LookupMessage);
        Assert.False(vm.LookingUp);
        // Form fields stay empty — the prefill path is skipped because the
        // user is being told "you already own this book" instead.
        Assert.Null(vm.BookInput.Title);
        Assert.Empty(vm.WorkInput.Authors);
        // Open Library shouldn't have been hit.
        await _lookup.DidNotReceiveWithAnyArgs().LookupByIsbnAsync(default!, default);
    }

    [Fact]
    public async Task LookupAsync_QueriesTheCleanedIsbn()
    {
        // The existing-edition check strips separators and upper-cases the
        // check digit before matching; the upstream lookup still gets the
        // ISBN as typed.
        var vm = CreateVm();
        vm.LookupIsbn = " 0-345-39180-x ";

        await vm.LookupAsync();

        await _dispatcher.Received(1).Query(
            Arg.Is<FindEditionByIsbn>(q => q!.Isbn == "034539180X"), Arg.Any<CancellationToken>());
        await _lookup.Received(1).LookupByIsbnAsync(" 0-345-39180-x ", Arg.Any<CancellationToken>());
        Assert.Null(vm.ExistingBook);
    }

    [Fact]
    public async Task LookupAsync_BlankIsbn_AsksForOneWithoutQuerying()
    {
        var vm = CreateVm();
        vm.LookupIsbn = "  ";

        await vm.LookupAsync();

        Assert.Equal("Enter an ISBN to look up.", vm.LookupMessage);
        Assert.Empty(_dispatcher.ReceivedCalls());
    }

    [Fact]
    public async Task AddCopyToExistingAsync_SendsAddCopyToFlaggedEdition()
    {
        _dispatcher.Query(Arg.Any<FindEditionByIsbn>(), Arg.Any<CancellationToken>())
            .Returns(HobbitMatch());
        var vm = CreateVm();
        vm.LookupIsbn = "9780345391803";
        await vm.LookupAsync();
        vm.CopyInput.Condition = BookCondition.Fine;

        var bookId = await vm.AddCopyToExistingAsync();

        Assert.Equal(4, bookId);
        Assert.False(vm.AddingCopy);
        await _dispatcher.Received(1).Send(
            Arg.Is<AddCopyToEdition>(c => c!.EditionId == 5
                && c.Condition == BookCondition.Fine
                && c.DateAcquired == null
                && c.Notes == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddCopyToExistingAsync_NoExistingBook_ReturnsNullWithoutSending()
    {
        var vm = CreateVm();

        var bookId = await vm.AddCopyToExistingAsync();

        Assert.Null(bookId);
        Assert.Empty(_dispatcher.ReceivedCalls());
    }

    [Fact]
    public async Task SaveAsync_SingleWork_SendsWorkFieldsWithParsedDate()
    {
        var vm = CreateVm();
        vm.BookInput.Title = "The Hobbit";
        vm.WorkInput.Title = "The Hobbit";
        vm.WorkInput.Subtitle = "There and Back Again";
        vm.WorkInput.Authors = ["J.R.R. Tolkien"];
        vm.WorkInput.FirstPublishedDate = "21 Sep 1937";
        vm.EditionInput.Isbn = "9780345391803";

        await vm.SaveAsync([3, 4]);

        var command = SentCreateBook();
        var work = Assert.IsType<SingleWork>(command.Works);
        Assert.Equal("The Hobbit", work.Title);
        Assert.Equal("There and Back Again", work.Subtitle);
        Assert.Equal(["J.R.R. Tolkien"], work.Authors);
        Assert.Equal(new DateOnly(1937, 9, 21), work.FirstPublished);
        Assert.Equal(DatePrecision.Day, work.Precision);
        // The page's genre picker selection belongs to the single Work.
        Assert.Equal([3, 4], work.GenreIds);
        Assert.Equal("9780345391803", command.Edition.Isbn);
    }

    [Fact]
    public async Task SaveAsync_SingleWorkWithoutTitle_FallsBackToBookTitle()
    {
        var vm = CreateVm();
        vm.BookInput.Title = "The Hobbit";
        vm.WorkInput.Authors = ["J.R.R. Tolkien"];

        await vm.SaveAsync([]);

        Assert.Equal("The Hobbit", Assert.IsType<SingleWork>(SentCreateBook().Works).Title);
    }

    [Fact]
    public async Task SaveAsync_DefaultsNewBookToUnread()
    {
        // New books default to Unread (the user marks Read as they actually
        // read them). The Add form never touches BookInput.Status here, so
        // this asserts the BookFormInput default reaches the command.
        var vm = CreateVm();
        vm.BookInput.Title = "The Hobbit";
        vm.WorkInput.Title = "The Hobbit";
        vm.WorkInput.Authors = ["J.R.R. Tolkien"];

        await vm.SaveAsync(new List<int>());

        Assert.Equal(BookStatus.Unread, SentCreateBook().Book.Status);
    }

    [Fact]
    public async Task SaveAsync_SendsBookEditionCopyAndPublisherFields()
    {
        var vm = CreateVm();
        vm.BookInput.Title = "Mort";
        vm.BookInput.Category = BookCategory.NonFiction;
        vm.BookInput.Status = BookStatus.Read;
        vm.BookInput.Rating = 4;
        vm.BookInput.Notes = "signed";
        vm.BookInput.DefaultCoverArtUrl = "https://covers.example.org/mort.jpg";
        vm.WorkInput.Authors = ["Terry Pratchett"];
        vm.EditionInput.Publisher = "Gollancz";
        vm.EditionInput.DatePrinted = "Oct 1987";
        vm.EditionInput.CoverUrl = "https://covers.example.org/mort-ed.jpg";
        vm.CopyInput.Condition = BookCondition.Fair;

        await vm.SaveAsync([]);

        var command = SentCreateBook();
        Assert.Equal(
            new NewBook("Mort", BookCategory.NonFiction, BookStatus.Read, 4, "signed", "https://covers.example.org/mort.jpg"),
            command.Book);
        Assert.Equal("Gollancz", command.Edition.PublisherName);
        Assert.Equal(new DateOnly(1987, 10, 1), command.Edition.DatePrinted);
        Assert.Equal(DatePrecision.Month, command.Edition.DatePrintedPrecision);
        Assert.Equal("https://covers.example.org/mort-ed.jpg", command.Edition.CoverUrl);
        Assert.Equal(BookCondition.Fair, command.CopyCondition);
    }

    [Fact]
    public async Task SaveAsync_MultipleAuthors_SendsAuthorsInChipOrder()
    {
        var vm = CreateVm();
        vm.BookInput.Title = "Relic";
        vm.WorkInput.Title = "Relic";
        vm.WorkInput.Authors = ["Douglas Preston", "Lincoln Child"];
        vm.EditionInput.Isbn = "9780812543261";

        await vm.SaveAsync(new List<int>());

        Assert.Equal(["Douglas Preston", "Lincoln Child"], Assert.IsType<SingleWork>(SentCreateBook().Works).Authors);
    }

    [Fact]
    public async Task SaveAsync_WithAcceptedExistingSeries_SendsSeriesIdAndOrder()
    {
        // The lookup names a series that already exists locally →
        // ApiMatchExisting; accepting it pins the existing id (no create).
        GivenLookup("9780552134613", "Sourcery", "Terry Pratchett", "Discworld", 5);
        _dispatcher.Query(Arg.Is<GetSeriesMatchCandidates>(q => q!.SeriesName == "Discworld"), Arg.Any<CancellationToken>())
            .Returns(new SeriesMatchCandidates(new SeriesCandidate(7, "Discworld", SeriesType.Series), [], 0));

        var vm = CreateVm();
        vm.LookupIsbn = "9780552134613";
        await vm.LookupAsync();

        Assert.NotNull(vm.SeriesSuggestion);
        Assert.Equal(MatchReason.ApiMatchExisting, vm.SeriesSuggestion!.Reason);

        await vm.AcceptSeriesSuggestion();
        Assert.Equal("Discworld", vm.AcceptedSeriesName); // filled the chosen-series field

        await vm.SaveAsync(new List<int>());

        Assert.Equal(new SeriesChoice(7, "Discworld", "5"), SentCreateBook().Series);
        Assert.Empty(SentEnsureSeries()); // attached to the existing one, nothing created
    }

    [Fact]
    public async Task AcceptSeriesSuggestion_NewSeries_EagerCreatesRowAndPinsId()
    {
        // TD-15a: accepting a "new series" suggestion creates the Series row at
        // the accept gesture (not deferred to save) and pins its id, so the save
        // attaches by id rather than find-or-creating.
        GivenLookup("9780765326355", "The Way of Kings", "Brandon Sanderson", "The Stormlight Archive", 1);
        GivenEnsureSeries("The Stormlight Archive", 21);

        var vm = CreateVm();
        vm.LookupIsbn = "9780765326355";
        await vm.LookupAsync();
        Assert.Equal(MatchReason.ApiMatchNewSeries, vm.SeriesSuggestion!.Reason);
        Assert.Null(vm.AcceptedSeriesId); // nothing created yet
        Assert.Empty(SentEnsureSeries());

        await vm.AcceptSeriesSuggestion();

        Assert.Equal(21, vm.AcceptedSeriesId); // pinned by the eager create
        Assert.Equal("The Stormlight Archive", Assert.Single(SentEnsureSeries()).Name);
    }

    [Fact]
    public async Task SaveAsync_WithAcceptedNewSeries_SendsThePinnedSeries()
    {
        GivenLookup("9780765326355", "The Way of Kings", "Brandon Sanderson", "The Stormlight Archive", 1);
        GivenEnsureSeries("The Stormlight Archive", 21);

        var vm = CreateVm();
        vm.LookupIsbn = "9780765326355";
        await vm.LookupAsync();

        Assert.Equal(MatchReason.ApiMatchNewSeries, vm.SeriesSuggestion!.Reason);
        await vm.AcceptSeriesSuggestion();

        // LookupAsync prefills WorkInput from the result, so we just save.
        await vm.SaveAsync(new List<int>());

        var command = SentCreateBook();
        Assert.Equal(new SeriesChoice(21, "The Stormlight Archive", "1"), command.Series);
        var work = Assert.IsType<SingleWork>(command.Works);
        Assert.Equal("The Way of Kings", work.Title);
        Assert.Equal(["Brandon Sanderson"], work.Authors);
    }

    [Fact]
    public async Task SaveAsync_EagerSeriesCreateFailed_SendsTheNameForTheSaveToCreate()
    {
        // A swallowed eager-create fault leaves the id null; the save still
        // carries the name so the handler's find-or-create net makes the row.
        GivenLookup("9780765326355", "The Way of Kings", "Brandon Sanderson", "The Stormlight Archive", 1);
        _dispatcher.Send(Arg.Any<EnsureSeries>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("transient"));

        var vm = CreateVm();
        vm.LookupIsbn = "9780765326355";
        await vm.LookupAsync();
        await vm.AcceptSeriesSuggestion();

        Assert.Null(vm.AcceptedSeriesId);
        await vm.SaveAsync(new List<int>());

        Assert.Equal(new SeriesChoice(null, "The Stormlight Archive", "1"), SentCreateBook().Series);
    }

    [Fact]
    public async Task SaveAsync_WithoutAcceptedSuggestion_SendsNoSeries()
    {
        // Same setup as the new-series test, but DON'T call Accept.
        GivenLookup("9780765326355", "The Way of Kings", "Brandon Sanderson", "The Stormlight Archive", 1);

        var vm = CreateVm();
        vm.LookupIsbn = "9780765326355";
        await vm.LookupAsync();

        await vm.SaveAsync(new List<int>());

        Assert.Equal(new SeriesChoice(null, null, null), SentCreateBook().Series);
        Assert.Empty(SentEnsureSeries());
    }

    // --- Manual series entry on single Add Book (the typeahead, not the
    // lookup-suggestion banner). Resolves an existing series with no create
    // round-trip, eager-creates a genuinely new one, and feeds the same save
    // path as the suggestion-accept flow. ---

    [Fact]
    public async Task InitializeAsync_CachesExistingSeriesInQueryOrder()
    {
        // GetSeriesOptions returns the series ordered by name
        // (GetSeriesOptionsHandlerTests); the cache keeps that order.
        GivenSeriesOptions(
            new SeriesPickerOption(2, "Discworld", SeriesType.Series),
            new SeriesPickerOption(1, "Mistborn", SeriesType.Series));

        var vm = CreateVm();
        await vm.InitializeAsync();

        Assert.Equal(
            [new BookAddViewModel.SeriesOption(2, "Discworld"), new BookAddViewModel.SeriesOption(1, "Mistborn")],
            vm.ExistingSeries);
    }

    [Fact]
    public async Task OnSeriesChosenAsync_NewName_EagerCreatesAndPinsId()
    {
        // Selecting the "Add …" row is the explicit create gesture — the Series
        // row is created and pinned at the selection, not deferred to save.
        GivenEnsureSeries("The Stormlight Archive", 31);
        var vm = CreateVm();
        await vm.InitializeAsync(); // empty cache

        await vm.OnSeriesChosenAsync("The Stormlight Archive");

        Assert.Equal(31, vm.AcceptedSeriesId); // eager-created + pinned
        Assert.Equal("The Stormlight Archive", Assert.Single(SentEnsureSeries()).Name);
        // Appended to the cache so a re-pick is a no-op.
        Assert.Contains(vm.ExistingSeries, s => s.Name == "The Stormlight Archive" && s.Id == 31);
    }

    [Fact]
    public async Task OnSeriesChosenAsync_ExistingCachedName_PinsIdWithNoCreate()
    {
        GivenSeriesOptions(new SeriesPickerOption(5, "Discworld", SeriesType.Series));

        var vm = CreateVm();
        await vm.InitializeAsync();

        await vm.OnSeriesChosenAsync("discworld"); // case clash

        Assert.Equal(5, vm.AcceptedSeriesId); // resolved from the cache
        Assert.Empty(SentEnsureSeries());       // no create round-trip
    }

    [Fact]
    public async Task OnSeriesChosenAsync_Blank_ClearsSelection()
    {
        GivenEnsureSeries("Mistborn", 1);
        var vm = CreateVm();
        await vm.InitializeAsync();
        await vm.OnSeriesChosenAsync("Mistborn");
        Assert.NotNull(vm.AcceptedSeriesId);

        // The Clearable X commits a null selection.
        await vm.OnSeriesChosenAsync("   ");

        Assert.Null(vm.AcceptedSeriesId);
        Assert.Null(vm.AcceptedSeriesName);
    }

    [Fact]
    public async Task OnSeriesChosenAsync_DifferentSeries_ReResolvesIdAndClearsOrder()
    {
        // Choosing a different series re-resolves the id AND drops the order — the
        // order belonged to the previous series and must not bleed onto the new
        // one. This is the structural fix for the old carryover bug: there is no
        // free-edit of a committed name, so a stale order can't survive a swap.
        GivenEnsureSeries("Mistborn", 1);
        GivenEnsureSeries("Elantris", 2);
        var vm = CreateVm();
        await vm.InitializeAsync();
        await vm.OnSeriesChosenAsync("Mistborn");
        Assert.Equal(1, vm.AcceptedSeriesId);
        vm.AcceptedSeriesOrderLabel = "3";

        await vm.OnSeriesChosenAsync("Elantris");

        Assert.Equal("Elantris", vm.AcceptedSeriesName);
        Assert.Equal(2, vm.AcceptedSeriesId);      // a different series row
        Assert.Null(vm.AcceptedSeriesOrderLabel);  // the "3" did not carry over
    }

    [Fact]
    public async Task OnSeriesChosenAsync_SameName_KeepsOrder()
    {
        // Re-confirming the same series (no change) must not wipe a set order.
        GivenEnsureSeries("Mistborn", 1);
        var vm = CreateVm();
        await vm.InitializeAsync();
        await vm.OnSeriesChosenAsync("Mistborn");
        vm.AcceptedSeriesOrderLabel = "3";

        await vm.OnSeriesChosenAsync("Mistborn");

        Assert.Equal("3", vm.AcceptedSeriesOrderLabel);
    }

    [Fact]
    public async Task SaveAsync_AcceptThenChooseDifferentSeries_DoesNotCarryStaleOrder()
    {
        // End-to-end carryover guard: "Use this" fills Mistborn #3, then the user
        // picks a different series in the typeahead. The book must attach to the
        // new series with NO order — the "3" was Mistborn's.
        GivenLookup("9780765326355", "The Way of Kings", "Brandon Sanderson", "Mistborn", 3);
        GivenEnsureSeries("Mistborn", 1);
        GivenEnsureSeries("Discworld", 2);

        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.LookupIsbn = "9780765326355";
        await vm.LookupAsync();
        await vm.AcceptSeriesSuggestion();     // Mistborn #3 filled
        Assert.Equal("3", vm.AcceptedSeriesOrderLabel);

        await vm.OnSeriesChosenAsync("Discworld"); // explicit swap → order drops

        await vm.SaveAsync(new List<int>());

        Assert.Equal(new SeriesChoice(2, "Discworld", null), SentCreateBook().Series);
    }

    [Fact]
    public async Task SaveAsync_WithManualSeries_SendsTheEagerCreatedSeries()
    {
        // Lookup carries no series; the user picks/creates one manually.
        GivenLookup("9780765326355", "The Way of Kings", "Brandon Sanderson", null, null);
        GivenEnsureSeries("The Stormlight Archive", 21);

        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.LookupIsbn = "9780765326355";
        await vm.LookupAsync();

        await vm.OnSeriesChosenAsync("The Stormlight Archive"); // explicit "Add …"
        vm.AcceptedSeriesOrderLabel = "1"; // user types the order after choosing

        await vm.SaveAsync(new List<int>());

        Assert.Equal(new SeriesChoice(21, "The Stormlight Archive", "1"), SentCreateBook().Series);
    }

    [Fact]
    public async Task SaveAsync_CollectionMode_SendsRowsInOrderWithAttachedWorkIds()
    {
        // Drew's Lovecraft "complete works" case during initial capture: a
        // brand-new anthology where some stories are new and some are already
        // in the library. Every row goes to the handler in on-screen order;
        // attach rows carry the picked Work's id.
        var vm = CreateVm();
        vm.BookInput.Title = "Mixed Anthology";
        vm.IsCollection = true;
        vm.CollectionWorks =
        [
            new() { Title = "New Story A", Authors = ["H.P. Lovecraft"] },
            new() { AttachedWorkId = 42, Title = "The Call of Cthulhu" },
            new() { Title = "New Story B", Authors = ["H.P. Lovecraft"] },
        ];

        var bookId = await vm.SaveAsync(selectedGenreIds: []);

        Assert.Equal(NewBookId, bookId);
        var collection = Assert.IsType<WorkCollection>(SentCreateBook().Works);
        Assert.Equal(["New Story A", "The Call of Cthulhu", "New Story B"], collection.Rows.Select(r => r.Title));
        Assert.Equal(new int?[] { null, 42, null }, collection.Rows.Select(r => r.AttachedWorkId));
        Assert.Equal(["H.P. Lovecraft"], collection.Rows[0].Authors);
    }

    [Fact]
    public void AttachExistingToRow_FlipsRowToAttachMode_AndStashesDisplayFields()
    {
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.CollectionWorks = [new() { Title = "user-typed" }];
        var picked = new WorkSearchResult(
            Id: 42, Title: "The Call of Cthulhu", Subtitle: null,
            AuthorName: "H.P. Lovecraft", FirstPublishedYear: 1928, BookCount: 3);

        vm.AttachExistingToRow(0, picked);

        Assert.Equal(42, vm.CollectionWorks[0].AttachedWorkId);
        Assert.Equal("H.P. Lovecraft", vm.CollectionWorks[0].AttachedWorkAuthor);
        // Title mirrors the existing Work's title so the summary card
        // and any subsequent save-then-edit round trip render it.
        Assert.Equal("The Call of Cthulhu", vm.CollectionWorks[0].Title);
        Assert.True(vm.HasAttachedWorkRows);
    }

    [Fact]
    public void DetachRow_ClearsAttachedFields_AndPreservesUserTitle()
    {
        // "Edit as new" affordance — user changes their mind after picking.
        // AttachedWorkId clears so the row returns to editable mode; the
        // title stays as whatever was last on the row (the picked Work's
        // title, which the user can edit / replace).
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.CollectionWorks = [new() { Title = "Existing", AttachedWorkId = 42, AttachedWorkAuthor = "Author" }];

        vm.DetachRow(0);

        Assert.Null(vm.CollectionWorks[0].AttachedWorkId);
        Assert.Null(vm.CollectionWorks[0].AttachedWorkAuthor);
        Assert.Equal("Existing", vm.CollectionWorks[0].Title);
        Assert.False(vm.HasAttachedWorkRows);
    }

    [Fact]
    public async Task SaveAsync_CollectionMode_SendsParsedRowFields()
    {
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.BookInput.Title = "The Bachman Books";
        vm.EditionInput.Isbn = "9780451178121";
        vm.CollectionWorks =
        [
            new() { Title = "Rage", Authors = ["Richard Bachman"] },
            new() { Title = "The Long Walk", Authors = ["Richard Bachman"], Subtitle = "A novel" },
            new() { Title = "Roadwork", Authors = ["Richard Bachman"], FirstPublishedDate = "1981",
                Contributors = [new ContributorEntry("Someone", AuthorRole.Illustrator)] },
        ];

        await vm.SaveAsync(new List<int>());

        var command = SentCreateBook();
        Assert.Equal("The Bachman Books", command.Book.Title);
        Assert.Equal("9780451178121", command.Edition.Isbn);
        var collection = Assert.IsType<WorkCollection>(command.Works);
        Assert.Equal(3, collection.Rows.Count);
        Assert.Equal("A novel", collection.Rows[1].Subtitle);
        var roadwork = collection.Rows[2];
        Assert.Equal(new DateOnly(1981, 1, 1), roadwork.FirstPublished);
        Assert.Equal(DatePrecision.Year, roadwork.Precision);
        Assert.Equal([new ContributorInput("Someone", AuthorRole.Illustrator)], roadwork.Contributors);
        Assert.Null(collection.Rows[0].FirstPublished);
    }

    [Fact]
    public async Task SaveAsync_NonCollection_EditorOnly_SendsContributorsWithRoles()
    {
        // Dictionary / Oxford Companion case — Work has an editor but no author.
        var vm = CreateVm();
        vm.BookInput.Title = "Concise Oxford English Dictionary";
        vm.WorkInput.Title = "Concise Oxford English Dictionary";
        vm.WorkInput.Authors = [];
        vm.WorkInput.Contributors =
        [
            new ContributorEntry { Name = "Catherine Soanes", Role = AuthorRole.Editor },
        ];

        await vm.SaveAsync(selectedGenreIds: []);

        var work = Assert.IsType<SingleWork>(SentCreateBook().Works);
        Assert.Empty(work.Authors);
        Assert.Equal([new ContributorInput("Catherine Soanes", AuthorRole.Editor)], work.Contributors);
    }

    [Fact]
    public async Task LookupAsync_CollectionMode_FillsBookOnlyAndSkipsWorkFields()
    {
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780451178121", Title: "The Bachman Books", Subtitle: "Four Early Novels",
                Author: "Richard Bachman", Publisher: "NAL",
                GenreCandidates: ["Horror"], DatePrinted: null, CoverUrl: "https://example.invalid/cover.jpg",
                Source: "Open Library",
                Series: null, SeriesNumber: null, SeriesNumberRaw: null));

        var vm = CreateVm();
        vm.IsCollection = true;
        vm.LookupIsbn = "9780451178121";

        await vm.LookupAsync();

        // Book-level fields prefilled (the Book represents the collection).
        Assert.Equal("The Bachman Books", vm.BookInput.Title);
        Assert.Equal("https://example.invalid/cover.jpg", vm.BookInput.DefaultCoverArtUrl);
        Assert.Equal("9780451178121", vm.EditionInput.Isbn);
        // Work-level fields untouched — the lookup describes the collection,
        // not its constituent works.
        Assert.Null(vm.WorkInput.Title);
        Assert.Empty(vm.WorkInput.Authors);
        // Genre candidates and series suggestion suppressed in collection mode.
        Assert.Empty(vm.LookupCandidates);
        Assert.Null(vm.SeriesSuggestion);
    }

    [Fact]
    public async Task AcceptSeriesSuggestion_OnLocalFallbackReason_IsNoOp()
    {
        // An author with 2+ ungrouped books → MatchReason.AuthorHasMultipleBooks
        // (a local fallback). Accept should be ignored on this reason because
        // the suggestion names no concrete series to attach to.
        _dispatcher.Query(Arg.Is<GetSeriesMatchCandidates>(q => q!.Author == "Some Author"), Arg.Any<CancellationToken>())
            .Returns(new SeriesMatchCandidates(null, [], 2));
        GivenLookup("9780000000000", "Book C", "Some Author", null, null);

        var vm = CreateVm();
        vm.LookupIsbn = "9780000000000";
        await vm.LookupAsync();

        Assert.Equal(MatchReason.AuthorHasMultipleBooks, vm.SeriesSuggestion!.Reason);

        await vm.AcceptSeriesSuggestion();
        // Accept is a no-op on a local-fallback reason — nothing chosen.
        Assert.Null(vm.AcceptedSeriesId);
        Assert.Null(vm.AcceptedSeriesName);
        Assert.Empty(SentEnsureSeries());
    }
}
