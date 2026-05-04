using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

public class BookAddViewModelTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();

    private BookAddViewModel CreateVm() =>
        new(_factory, _lookup, new SeriesMatchService(_factory));

    private GenrePickerViewModel CreateGenrePicker() => new(_factory);

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

        await vm.ApplyCandidateAsync(candidate, CreateGenrePicker());

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

        await vm.ApplyCandidateAsync(candidate, CreateGenrePicker());

        Assert.Equal("User typed this", vm.BookInput.Title);
        Assert.Equal(new[] { "User author" }, vm.WorkInput.Authors);
        Assert.Equal("15 Jun 1939", vm.WorkInput.FirstPublishedDate);
    }

    [Fact]
    public async Task SaveAsync_BlankIsbnPersistsAsNull()
    {
        var vm = CreateVm();
        vm.BookInput.Title = "And Then There Were None";
        vm.WorkInput.Title = "And Then There Were None";
        vm.WorkInput.Authors = ["Agatha Christie"];
        vm.EditionInput.Isbn = "";

        var ok = await vm.SaveAsync(new List<int>());

        Assert.NotNull(ok);
        using var db = _factory.CreateDbContext();
        var edition = db.Editions.Single();
        Assert.Null(edition.Isbn);
    }

    [Fact]
    public async Task SaveAsync_TwoNoIsbnEditions_CoexistWithoutCollision()
    {
        // Verifies the filtered unique index actually permits multiple null
        // ISBNs. With an unfiltered unique index this would throw on the
        // second save.
        var vm1 = CreateVm();
        vm1.BookInput.Title = "Book A";
        vm1.WorkInput.Title = "Book A";
        vm1.WorkInput.Authors = ["Author A"];
        Assert.NotNull(await vm1.SaveAsync(new List<int>()));

        var vm2 = CreateVm();
        vm2.BookInput.Title = "Book B";
        vm2.WorkInput.Title = "Book B";
        vm2.WorkInput.Authors = ["Author B"];
        Assert.NotNull(await vm2.SaveAsync(new List<int>()));

        using var db = _factory.CreateDbContext();
        Assert.Equal(2, db.Editions.Count(e => e.Isbn == null));
    }

    [Fact]
    public async Task LookupAsync_ExistingIsbn_FlagsExistingBookInsteadOfPrefilling()
    {
        // Seed an existing book with the ISBN we're about to look up.
        using (var db = _factory.CreateDbContext())
        {
            db.Books.Add(new Book
            {
                Title = "The Hobbit",
                Works = [new Work { Title = "The Hobbit", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "Tolkien" }, Order = 0 }] }],
                Editions = [new Edition { Isbn = "9780345391803", Copies = [new Copy { Condition = BookCondition.Good }] }]
            });
            await db.SaveChangesAsync();
        }

        var vm = CreateVm();
        vm.LookupIsbn = "9780345391803";

        await vm.LookupAsync(CreateGenrePicker());

        Assert.NotNull(vm.ExistingBook);
        Assert.Equal("The Hobbit", vm.ExistingBook!.Title);
        Assert.Equal(1, vm.ExistingBook.CopyCount);
        // Form fields stay empty — the prefill path is skipped because the
        // user is being told "you already own this book" instead.
        Assert.Null(vm.BookInput.Title);
        Assert.Empty(vm.WorkInput.Authors);
        // Open Library shouldn't have been hit.
        await _lookup.DidNotReceiveWithAnyArgs().LookupByIsbnAsync(default!, default);
    }

    [Fact]
    public async Task AddCopyToExistingAsync_AppendsCopyToFlaggedEdition()
    {
        int editionId;
        using (var db = _factory.CreateDbContext())
        {
            var book = new Book
            {
                Title = "The Hobbit",
                Works = [new Work { Title = "The Hobbit", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "Tolkien" }, Order = 0 }] }],
                Editions = [new Edition { Isbn = "9780345391803", Copies = [new Copy { Condition = BookCondition.Good }] }]
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            editionId = book.Editions[0].Id;
        }

        var vm = CreateVm();
        vm.LookupIsbn = "9780345391803";
        await vm.LookupAsync(CreateGenrePicker());

        var bookId = await vm.AddCopyToExistingAsync();

        Assert.NotNull(bookId);
        using var db2 = _factory.CreateDbContext();
        Assert.Equal(2, db2.Copies.Count(c => c.EditionId == editionId));
    }

    [Fact]
    public async Task SaveAsync_CreatesBookAndWorkTogether()
    {
        var vm = CreateVm();
        vm.BookInput.Title = "The Hobbit";
        vm.WorkInput.Title = "The Hobbit";
        vm.WorkInput.Authors = ["J.R.R. Tolkien"];
        vm.WorkInput.FirstPublishedDate = "21 Sep 1937";
        vm.EditionInput.Isbn = "9780345391803";

        var ok = await vm.SaveAsync(new List<int>());

        Assert.NotNull(ok);
        using var db = _factory.CreateDbContext();
        var book = db.Books.Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author).Single();
        var work = Assert.Single(book.Works);
        Assert.Equal("The Hobbit", work.Title);
        Assert.Equal("J.R.R. Tolkien", work.WorkAuthors.OrderBy(wa => wa.Order).First().Author.Name);
        Assert.Equal(new DateOnly(1937, 9, 21), work.FirstPublishedDate);
        Assert.Equal(DatePrecision.Day, work.FirstPublishedDatePrecision);
    }

    [Fact]
    public async Task SaveAsync_MultipleAuthors_DualWritesLeadAndJoin()
    {
        // Multi-author cutover PR1: save path sets Work.Author = lead chip
        // (legacy AuthorId for backwards compat) AND populates Work.WorkAuthors
        // with all chips, Order ascending. Co-author becomes a real Author row
        // via find-or-create.
        var vm = CreateVm();
        vm.BookInput.Title = "Relic";
        vm.WorkInput.Title = "Relic";
        vm.WorkInput.Authors = ["Douglas Preston", "Lincoln Child"];
        vm.EditionInput.Isbn = "9780812543261";

        var ok = await vm.SaveAsync(new List<int>());

        Assert.NotNull(ok);
        using var db = _factory.CreateDbContext();
        var work = db.Works
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Single();
        Assert.Equal(2, work.WorkAuthors.Count);
        var ordered = work.WorkAuthors.OrderBy(wa => wa.Order).ToList();
        Assert.Equal("Douglas Preston", ordered[0].Author.Name);
        Assert.Equal(0, ordered[0].Order);
        Assert.Equal("Lincoln Child", ordered[1].Author.Name);
        Assert.Equal(1, ordered[1].Order);
        // Both authors exist as canonical Author rows.
        Assert.Equal(2, db.Authors.Count(a => a.Name == "Douglas Preston" || a.Name == "Lincoln Child"));
    }

    [Fact]
    public async Task SaveAsync_WithAcceptedExistingSeries_AttachesWorkBySeriesId()
    {
        // Seed a Series so the lookup-driven match returns ApiMatchExisting.
        int seededSeriesId;
        using (var db = _factory.CreateDbContext())
        {
            var series = new Series { Name = "Discworld", Type = SeriesType.Series };
            db.Series.Add(series);
            await db.SaveChangesAsync();
            seededSeriesId = series.Id;
        }

        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780552134613", Title: "Sourcery", Subtitle: null,
                Author: "Terry Pratchett", Publisher: null,
                GenreCandidates: [], DatePrinted: null, CoverUrl: null,
                Source: "Open Library",
                Series: "Discworld", SeriesNumber: 5, SeriesNumberRaw: "5"));

        var vm = CreateVm();
        vm.LookupIsbn = "9780552134613";
        await vm.LookupAsync(CreateGenrePicker());

        // Sanity: suggestion should be the existing-series flavour.
        Assert.NotNull(vm.SeriesSuggestion);
        Assert.Equal(MatchReason.ApiMatchExisting, vm.SeriesSuggestion!.Reason);

        vm.AcceptSeriesSuggestion();
        Assert.True(vm.SeriesSuggestionAccepted);

        await vm.SaveAsync(new List<int>());

        using var db2 = _factory.CreateDbContext();
        var work = db2.Works.Include(w => w.Series).Single();
        Assert.Equal(seededSeriesId, work.SeriesId);
        Assert.Equal(5, work.SeriesOrder);
        // No new Series row created — attached to the existing one.
        Assert.Equal(1, db2.Series.Count());
    }

    [Fact]
    public async Task SaveAsync_WithAcceptedNewSeries_FindOrCreatesSeriesAndAttaches()
    {
        // No Series rows seeded — lookup-driven match returns ApiMatchNewSeries.
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780765326355", Title: "The Way of Kings", Subtitle: null,
                Author: "Brandon Sanderson", Publisher: null,
                GenreCandidates: [], DatePrinted: null, CoverUrl: null,
                Source: "Open Library",
                Series: "The Stormlight Archive", SeriesNumber: 1, SeriesNumberRaw: "1"));

        var vm = CreateVm();
        vm.LookupIsbn = "9780765326355";
        await vm.LookupAsync(CreateGenrePicker());

        Assert.Equal(MatchReason.ApiMatchNewSeries, vm.SeriesSuggestion!.Reason);
        vm.AcceptSeriesSuggestion();

        // SaveAsync needs the form filled enough to construct the Work.
        // LookupAsync prefills WorkInput from the result, so we just save.
        await vm.SaveAsync(new List<int>());

        using var db = _factory.CreateDbContext();
        var series = Assert.Single(db.Series);
        Assert.Equal("The Stormlight Archive", series.Name);
        Assert.Equal(SeriesType.Series, series.Type); // Q1 default — not Collection.

        var work = db.Works.Include(w => w.Series).Single();
        Assert.Equal(series.Id, work.SeriesId);
        Assert.Equal(1, work.SeriesOrder);
    }

    [Fact]
    public async Task SaveAsync_WithoutAcceptedSuggestion_DoesNotAttachSeries()
    {
        // Same setup as the new-series test, but DON'T call Accept.
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780765326355", Title: "The Way of Kings", Subtitle: null,
                Author: "Brandon Sanderson", Publisher: null,
                GenreCandidates: [], DatePrinted: null, CoverUrl: null,
                Source: "Open Library",
                Series: "The Stormlight Archive", SeriesNumber: 1, SeriesNumberRaw: "1"));

        var vm = CreateVm();
        vm.LookupIsbn = "9780765326355";
        await vm.LookupAsync(CreateGenrePicker());

        // No Accept call.
        await vm.SaveAsync(new List<int>());

        using var db = _factory.CreateDbContext();
        Assert.Empty(db.Series);
        var work = db.Works.Single();
        Assert.Null(work.SeriesId);
        Assert.Null(work.SeriesOrder);
    }

    [Fact]
    public async Task AcceptSeriesSuggestion_OnLocalFallbackReason_IsNoOp()
    {
        // Seed: an author with 2+ ungrouped works → MatchReason.AuthorHasMultipleBooks
        // (a local fallback). Accept should be ignored on this reason because
        // the suggestion names no concrete series to attach to.
        using (var db = _factory.CreateDbContext())
        {
            var author = new Author { Name = "Some Author" };
            db.Books.AddRange(
                new Book { Title = "Book A", Works = [new Work { Title = "Book A", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }] },
                new Book { Title = "Book B", Works = [new Work { Title = "Book B", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780000000000", Title: "Book C", Subtitle: null,
                Author: "Some Author", Publisher: null,
                GenreCandidates: [], DatePrinted: null, CoverUrl: null,
                Source: "Open Library",
                Series: null, SeriesNumber: null, SeriesNumberRaw: null));

        var vm = CreateVm();
        vm.LookupIsbn = "9780000000000";
        await vm.LookupAsync(CreateGenrePicker());

        Assert.Equal(MatchReason.AuthorHasMultipleBooks, vm.SeriesSuggestion!.Reason);

        vm.AcceptSeriesSuggestion();
        // Accept call is a no-op — SeriesSuggestionAccepted stays false.
        Assert.False(vm.SeriesSuggestionAccepted);
        Assert.Null(vm.AcceptedSeriesId);
        Assert.Null(vm.AcceptedSeriesName);
    }
}
