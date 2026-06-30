using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Integration)]
public class BookAddViewModelTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();

    private BookAddViewModel CreateVm() =>
        new(_factory, _lookup, new SeriesMatchService(_factory), new WorkSearchService(_factory), TestDispatcher.For(_factory), NullLogger<BookAddViewModel>.Instance);

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
    public async Task SaveAsync_EditionNumberPersistsOnEdition()
    {
        // Capture flow lock: typing "3" into the Edition # field on
        // EditionCopyForm must round-trip onto Edition.EditionNumber.
        // Backs the Joy of Cooking 3rd ed. vs 9th ed. disambiguation
        // that motivated the column.
        var vm = CreateVm();
        vm.BookInput.Title = "Joy of Cooking";
        vm.WorkInput.Title = "Joy of Cooking";
        vm.WorkInput.Authors = ["Irma Rombauer"];
        vm.EditionInput.Format = BookFormat.Hardcover;
        vm.EditionInput.EditionNumber = 3;

        var bookId = await vm.SaveAsync(selectedGenreIds: []);

        Assert.NotNull(bookId);
        await using var db = _factory.CreateDbContext();
        var edition = await db.Editions.SingleAsync(e => e.BookId == bookId);
        Assert.Equal(3, edition.EditionNumber);
    }

    [Fact]
    public async Task SaveAsync_NullEditionNumberLeavesColumnNull()
    {
        // The common case — fiction reprints, mass-market paperbacks
        // where the edition number isn't on the cover. Field stays blank,
        // column stays null.
        var vm = CreateVm();
        vm.BookInput.Title = "Foundation";
        vm.WorkInput.Title = "Foundation";
        vm.WorkInput.Authors = ["Isaac Asimov"];
        vm.EditionInput.Format = BookFormat.MassMarketPaperback;
        // EditionInput.EditionNumber left as default (null).

        var bookId = await vm.SaveAsync(selectedGenreIds: []);

        Assert.NotNull(bookId);
        await using var db = _factory.CreateDbContext();
        var edition = await db.Editions.SingleAsync(e => e.BookId == bookId);
        Assert.Null(edition.EditionNumber);
    }

    [Fact]
    public async Task SaveAsync_SingleAuthorMode_AppliesSharedAuthorsToEveryWork()
    {
        // Single-Author mode is the "King's Different Seasons" shape —
        // user enters the author once at the top; save applies it to
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

        var bookId = await vm.SaveAsync(selectedGenreIds: []);

        Assert.NotNull(bookId);
        await using var db = _factory.CreateDbContext();
        var saved = await db.Books
            .Include(b => b.Works)
                .ThenInclude(w => w.WorkAuthors)
                .ThenInclude(wa => wa.Author)
            .FirstAsync(b => b.Id == bookId);

        Assert.Equal(4, saved.Works.Count);
        Assert.All(saved.Works, w =>
        {
            Assert.Single(w.WorkAuthors);
            Assert.Equal("Stephen King", w.WorkAuthors[0].Author.Name);
        });
    }

    [Fact]
    public async Task SaveAsync_SingleAuthorMode_NoSharedAuthors_Throws()
    {
        // Save in Single-Author mode with no shared authors set should
        // fail with a clear message rather than silently produce zero
        // works.
        var vm = CreateVm();
        vm.BookInput.Title = "Untitled Collection";
        vm.EditionInput.Format = BookFormat.TradePaperback;
        vm.IsCollection = true;
        vm.SingleAuthor = true;
        vm.SharedAuthors = [];
        vm.CollectionWorks = [new() { Title = "Story A" }];

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => vm.SaveAsync(selectedGenreIds: []));
        Assert.Contains("Single-Author", ex.Message);
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
    public async Task SaveAsync_SingleGenreMode_AppliesSharedGenresToEveryWork()
    {
        // Single-Genre mode is Drew's order-of-magnitude case — pick the
        // genre once at the top; save applies it to every Work row.
        int sfId;
        using (var db = _factory.CreateDbContext())
        {
            var sf = new Genre { Name = "Science Fiction" };
            db.Genres.Add(sf);
            await db.SaveChangesAsync();
            sfId = sf.Id;
        }

        var vm = CreateVm();
        vm.BookInput.Title = "The Mammoth Book of SF";
        vm.EditionInput.Format = BookFormat.TradePaperback;
        vm.IsCollection = true;
        vm.SingleAuthor = true;
        vm.SharedAuthors = new List<string> { "Various" };
        vm.SingleGenre = true;
        vm.SharedGenreIds = new List<int> { sfId };
        vm.CollectionWorks =
        [
            new() { Title = "Story A" },
            new() { Title = "Story B" },
            new() { Title = "Story C" },
        ];

        var bookId = await vm.SaveAsync(selectedGenreIds: []);

        Assert.NotNull(bookId);
        await using var db2 = _factory.CreateDbContext();
        var saved = await db2.Books
            .Include(b => b.Works).ThenInclude(w => w.Genres)
            .FirstAsync(b => b.Id == bookId);

        Assert.Equal(3, saved.Works.Count);
        Assert.All(saved.Works, w =>
        {
            var genre = Assert.Single(w.Genres);
            Assert.Equal(sfId, genre.Id);
        });
    }

    [Fact]
    public async Task SaveAsync_PerRowGenres_AttachesEachWorksOwnList()
    {
        // Single-Genre OFF: each row carries its own GenreIds. The save
        // path applies them per Work. This is the "mostly SF + one
        // outlier" shape after the user has overridden the inherited
        // genre on the odd row.
        int sfId, horrorId;
        using (var db = _factory.CreateDbContext())
        {
            var sf = new Genre { Name = "Science Fiction" };
            var horror = new Genre { Name = "Horror" };
            db.Genres.AddRange(sf, horror);
            await db.SaveChangesAsync();
            sfId = sf.Id;
            horrorId = horror.Id;
        }

        var vm = CreateVm();
        vm.BookInput.Title = "Mostly SF, One Horror";
        vm.EditionInput.Format = BookFormat.TradePaperback;
        vm.IsCollection = true;
        vm.SingleGenre = false;
        vm.CollectionWorks =
        [
            new() { Title = "SF One", Authors = ["Author A"], GenreIds = [sfId] },
            new() { Title = "SF Two", Authors = ["Author A"], GenreIds = [sfId] },
            new() { Title = "Horror One", Authors = ["Author A"], GenreIds = [horrorId] },
        ];

        var bookId = await vm.SaveAsync(selectedGenreIds: []);

        Assert.NotNull(bookId);
        await using var db2 = _factory.CreateDbContext();
        var saved = await db2.Books
            .Include(b => b.Works).ThenInclude(w => w.Genres)
            .FirstAsync(b => b.Id == bookId);

        Assert.Equal(3, saved.Works.Count);
        var sfOne = saved.Works.Single(w => w.Title == "SF One");
        Assert.Equal(sfId, Assert.Single(sfOne.Genres).Id);
        var horrorOne = saved.Works.Single(w => w.Title == "Horror One");
        Assert.Equal(horrorId, Assert.Single(horrorOne.Genres).Id);
    }

    [Fact]
    public async Task SaveAsync_SingleGenreMode_EmptySharedList_LeavesWorksGenreless()
    {
        // Single-Genre on but nothing picked — collection still saves; the
        // works just land with no genres (same shape as a non-collection
        // save with selectedGenreIds=[]). User can tag genres later.
        var vm = CreateVm();
        vm.BookInput.Title = "Untagged Collection";
        vm.EditionInput.Format = BookFormat.TradePaperback;
        vm.IsCollection = true;
        vm.SingleAuthor = true;
        vm.SharedAuthors = new List<string> { "Various" };
        vm.SingleGenre = true;
        vm.SharedGenreIds = [];
        vm.CollectionWorks =
        [
            new() { Title = "Story A" },
            new() { Title = "Story B" },
        ];

        var bookId = await vm.SaveAsync(selectedGenreIds: []);

        Assert.NotNull(bookId);
        await using var db = _factory.CreateDbContext();
        var saved = await db.Books
            .Include(b => b.Works).ThenInclude(w => w.Genres)
            .FirstAsync(b => b.Id == bookId);
        Assert.All(saved.Works, w => Assert.Empty(w.Genres));
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

        await vm.LookupAsync();

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
        await vm.LookupAsync();

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
    public async Task SaveAsync_DefaultsNewBookToUnread()
    {
        // New books default to Unread (the user marks Read as they actually
        // read them). The Add form never touches BookInput.Status here, so
        // this asserts the BookFormInput default carries through SaveAsync.
        var vm = CreateVm();
        vm.BookInput.Title = "The Hobbit";
        vm.WorkInput.Title = "The Hobbit";
        vm.WorkInput.Authors = ["J.R.R. Tolkien"];

        var ok = await vm.SaveAsync(new List<int>());

        Assert.NotNull(ok);
        using var db = _factory.CreateDbContext();
        var book = db.Books.Single();
        Assert.Equal(BookStatus.Unread, book.Status);
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
        await vm.LookupAsync();

        // Sanity: suggestion should be the existing-series flavour.
        Assert.NotNull(vm.SeriesSuggestion);
        Assert.Equal(MatchReason.ApiMatchExisting, vm.SeriesSuggestion!.Reason);

        await vm.AcceptSeriesSuggestion();
        Assert.Equal("Discworld", vm.AcceptedSeriesName); // filled the chosen-series field

        await vm.SaveAsync(new List<int>());

        using var db2 = _factory.CreateDbContext();
        var work = db2.Works.Include(w => w.Series).Single();
        Assert.Equal(seededSeriesId, work.SeriesId);
        Assert.Equal(5, work.SeriesOrder);
        // No new Series row created — attached to the existing one.
        Assert.Equal(1, db2.Series.Count());
    }

    [Fact]
    public async Task AcceptSeriesSuggestion_NewSeries_EagerCreatesRowAndPinsId()
    {
        // TD-15a: accepting a "new series" suggestion creates the Series row at
        // the accept gesture (not deferred to save) and pins its id, so the save
        // attaches by id rather than find-or-creating.
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780765326355", Title: "The Way of Kings", Subtitle: null,
                Author: "Brandon Sanderson", Publisher: null,
                GenreCandidates: [], DatePrinted: null, CoverUrl: null,
                Source: "Open Library",
                Series: "The Stormlight Archive", SeriesNumber: 1, SeriesNumberRaw: "1"));

        var vm = CreateVm();
        vm.LookupIsbn = "9780765326355";
        await vm.LookupAsync();
        Assert.Equal(MatchReason.ApiMatchNewSeries, vm.SeriesSuggestion!.Reason);
        Assert.Null(vm.AcceptedSeriesId); // nothing created yet

        await vm.AcceptSeriesSuggestion();

        Assert.NotNull(vm.AcceptedSeriesId); // pinned by the eager create
        using var db = _factory.CreateDbContext();
        var series = Assert.Single(db.Series); // the row exists BEFORE any save
        Assert.Equal("The Stormlight Archive", series.Name);
        Assert.Equal(vm.AcceptedSeriesId, series.Id);
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
        await vm.LookupAsync();

        Assert.Equal(MatchReason.ApiMatchNewSeries, vm.SeriesSuggestion!.Reason);
        await vm.AcceptSeriesSuggestion();

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
        await vm.LookupAsync();

        // No Accept call.
        await vm.SaveAsync(new List<int>());

        using var db = _factory.CreateDbContext();
        Assert.Empty(db.Series);
        var work = db.Works.Single();
        Assert.Null(work.SeriesId);
        Assert.Null(work.SeriesOrder);
    }

    // --- Manual series entry on single Add Book (the typeahead, not the
    // lookup-suggestion banner). Resolves an existing series with no create
    // round-trip, eager-creates a genuinely new one, and feeds the same save
    // path as the suggestion-accept flow. ---

    [Fact]
    public async Task InitializeAsync_CachesExistingSeriesOrderedByName()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Series.AddRange(
                new Series { Name = "Mistborn", Type = SeriesType.Series },
                new Series { Name = "Discworld", Type = SeriesType.Series });
            await db.SaveChangesAsync();
        }

        var vm = CreateVm();
        await vm.InitializeAsync();

        Assert.Equal(["Discworld", "Mistborn"], vm.ExistingSeries.Select(s => s.Name));
    }

    [Fact]
    public async Task OnSeriesChosenAsync_NewName_EagerCreatesAndPinsId()
    {
        // Selecting the "Add …" row is the explicit create gesture — the Series
        // row is created and pinned at the selection, not deferred to save.
        var vm = CreateVm();
        await vm.InitializeAsync(); // empty cache

        await vm.OnSeriesChosenAsync("The Stormlight Archive");

        Assert.NotNull(vm.AcceptedSeriesId); // eager-created + pinned
        using var db = _factory.CreateDbContext();
        var series = Assert.Single(db.Series); // row exists at the gesture
        Assert.Equal("The Stormlight Archive", series.Name);
        Assert.Equal(vm.AcceptedSeriesId, series.Id);
        // Appended to the cache so a re-pick is a no-op.
        Assert.Contains(vm.ExistingSeries, s => s.Name == "The Stormlight Archive");
    }

    [Fact]
    public async Task OnSeriesChosenAsync_ExistingCachedName_PinsIdWithNoDuplicate()
    {
        int seededId;
        using (var db = _factory.CreateDbContext())
        {
            var series = new Series { Name = "Discworld", Type = SeriesType.Series };
            db.Series.Add(series);
            await db.SaveChangesAsync();
            seededId = series.Id;
        }

        var vm = CreateVm();
        await vm.InitializeAsync();

        await vm.OnSeriesChosenAsync("discworld"); // case clash

        Assert.Equal(seededId, vm.AcceptedSeriesId); // resolved from the cache
        using var db2 = _factory.CreateDbContext();
        Assert.Equal(1, db2.Series.Count()); // no duplicate created
    }

    [Fact]
    public async Task OnSeriesChosenAsync_Blank_ClearsSelection()
    {
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
        var vm = CreateVm();
        await vm.InitializeAsync();
        await vm.OnSeriesChosenAsync("Mistborn");
        var firstId = vm.AcceptedSeriesId;
        Assert.NotNull(firstId);
        vm.AcceptedSeriesOrderLabel = "3";

        await vm.OnSeriesChosenAsync("Elantris");

        Assert.Equal("Elantris", vm.AcceptedSeriesName);
        Assert.NotNull(vm.AcceptedSeriesId);
        Assert.NotEqual(firstId, vm.AcceptedSeriesId); // a different series row
        Assert.Null(vm.AcceptedSeriesOrderLabel);       // the "3" did not carry over
    }

    [Fact]
    public async Task OnSeriesChosenAsync_SameName_KeepsOrder()
    {
        // Re-confirming the same series (no change) must not wipe a set order.
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
        // picks a different series in the typeahead. The work must attach to the
        // new series with NO order — the "3" was Mistborn's.
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780765326355", Title: "The Way of Kings", Subtitle: null,
                Author: "Brandon Sanderson", Publisher: null,
                GenreCandidates: [], DatePrinted: null, CoverUrl: null,
                Source: "Open Library",
                Series: "Mistborn", SeriesNumber: 3, SeriesNumberRaw: "3"));

        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.LookupIsbn = "9780765326355";
        await vm.LookupAsync();
        await vm.AcceptSeriesSuggestion();     // Mistborn #3 filled
        Assert.Equal("3", vm.AcceptedSeriesOrderLabel);

        await vm.OnSeriesChosenAsync("Discworld"); // explicit swap → order drops

        await vm.SaveAsync(new List<int>());

        using var db = _factory.CreateDbContext();
        var discworld = await db.Series.SingleAsync(s => s.Name == "Discworld");
        var work = db.Works.Include(w => w.Series).Single();
        Assert.Equal(discworld.Id, work.SeriesId);
        Assert.Null(work.SeriesOrder); // NOT 3 — that order was Mistborn's
    }

    [Fact]
    public async Task SaveAsync_WithManualSeries_AttachesWorkToEagerCreatedSeries()
    {
        // Lookup carries no series; the user picks/creates one manually.
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780765326355", Title: "The Way of Kings", Subtitle: null,
                Author: "Brandon Sanderson", Publisher: null,
                GenreCandidates: [], DatePrinted: null, CoverUrl: null,
                Source: "Open Library",
                Series: null, SeriesNumber: null, SeriesNumberRaw: null));

        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.LookupIsbn = "9780765326355";
        await vm.LookupAsync();

        await vm.OnSeriesChosenAsync("The Stormlight Archive"); // explicit "Add …"
        vm.AcceptedSeriesOrderLabel = "1"; // user types the order after choosing

        await vm.SaveAsync(new List<int>());

        using var db = _factory.CreateDbContext();
        var series = Assert.Single(db.Series);
        Assert.Equal("The Stormlight Archive", series.Name);
        var work = db.Works.Include(w => w.Series).Single();
        Assert.Equal(series.Id, work.SeriesId);
        Assert.Equal(1, work.SeriesOrder);
    }

    [Fact]
    public async Task SaveAsync_CollectionMode_MixedNewAndAttachedRows_AttachesExistingAndCreatesNew()
    {
        // Drew's Lovecraft "complete works" case during initial capture: a
        // brand-new anthology where some stories are new and some are
        // already in the library from previous anthologies. Save creates
        // the new Works and attaches the existing ones to the same Book,
        // preserving the user-entered row order.
        var factory = new TestDbContextFactory();
        int existingId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "H.P. Lovecraft" };
            var existing = new Work
            {
                Title = "The Call of Cthulhu",
                WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
            };
            db.Books.Add(new Book { Title = "Other Anthology", Works = [existing] });
            await db.SaveChangesAsync();
            existingId = existing.Id;
        }

        var vm = new BookAddViewModel(factory, _lookup, new SeriesMatchService(factory), new WorkSearchService(factory), TestDispatcher.For(factory), NullLogger<BookAddViewModel>.Instance);
        vm.BookInput.Title = "Mixed Anthology";
        vm.EditionInput.Format = BookFormat.TradePaperback;
        vm.IsCollection = true;
        vm.CollectionWorks =
        [
            new() { Title = "New Story A", Authors = ["H.P. Lovecraft"] },
            new() { AttachedWorkId = existingId, Title = "The Call of Cthulhu" },
            new() { Title = "New Story B", Authors = ["H.P. Lovecraft"] },
        ];

        var bookId = await vm.SaveAsync(selectedGenreIds: []);

        Assert.NotNull(bookId);
        await using var db2 = factory.CreateDbContext();
        var book = await db2.Books
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstAsync(b => b.Id == bookId);

        Assert.Equal(3, book.Works.Count);
        Assert.Contains(book.Works, w => w.Id == existingId);
        Assert.Contains(book.Works, w => w.Title == "New Story A");
        Assert.Contains(book.Works, w => w.Title == "New Story B");
        // Existing Work still has its original anthology attached too — N:N preserved.
        var cthulhu = await db2.Works.Include(w => w.Books).FirstAsync(w => w.Id == existingId);
        Assert.Equal(2, cthulhu.Books.Count);
    }

    [Fact]
    public async Task SaveAsync_CollectionMode_AttachOnlyRow_DoesNotRequireTitleOrAuthorOnThatRow()
    {
        // An attach-existing row carries no editable fields; validation
        // must skip the "title + at least one author" check for it. A
        // collection with a single attach-only row should save cleanly.
        var factory = new TestDbContextFactory();
        int existingId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Author" };
            var existing = new Work
            {
                Title = "Existing",
                WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
            };
            db.Books.Add(new Book { Title = "Other", Works = [existing] });
            await db.SaveChangesAsync();
            existingId = existing.Id;
        }

        var vm = new BookAddViewModel(factory, _lookup, new SeriesMatchService(factory), new WorkSearchService(factory), TestDispatcher.For(factory), NullLogger<BookAddViewModel>.Instance);
        vm.BookInput.Title = "Attach-only book";
        vm.EditionInput.Format = BookFormat.TradePaperback;
        vm.IsCollection = true;
        vm.CollectionWorks =
        [
            new() { AttachedWorkId = existingId, Title = "Existing" },
        ];

        var bookId = await vm.SaveAsync(selectedGenreIds: []);

        Assert.NotNull(bookId);
        using var db2 = factory.CreateDbContext();
        var book = db2.Books.Include(b => b.Works).Single(b => b.Id == bookId);
        Assert.Single(book.Works);
        Assert.Equal(existingId, book.Works[0].Id);
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
    public async Task SaveAsync_CollectionMode_CreatesBookWithMultipleWorks()
    {
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.BookInput.Title = "The Bachman Books";
        vm.EditionInput.Isbn = "9780451178121";
        vm.CollectionWorks =
        [
            new() { Title = "Rage", Authors = ["Richard Bachman"] },
            new() { Title = "The Long Walk", Authors = ["Richard Bachman"] },
            new() { Title = "Roadwork", Authors = ["Richard Bachman"], FirstPublishedDate = "1981" },
        ];

        var ok = await vm.SaveAsync(new List<int>());

        Assert.NotNull(ok);
        using var db = _factory.CreateDbContext();
        var book = db.Books
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Single();
        Assert.Equal("The Bachman Books", book.Title);
        Assert.Equal(3, book.Works.Count);
        // Each row's authors land in WorkAuthors. No designated "primary" Work.
        var rage = book.Works.Single(w => w.Title == "Rage");
        Assert.Equal("Richard Bachman", rage.WorkAuthors.Single().Author.Name);
        var roadwork = book.Works.Single(w => w.Title == "Roadwork");
        Assert.Equal(new DateOnly(1981, 1, 1), roadwork.FirstPublishedDate);
        Assert.Equal(DatePrecision.Year, roadwork.FirstPublishedDatePrecision);
    }

    [Fact]
    public async Task SaveAsync_CollectionMode_SkipsEmptyRows()
    {
        // The Add page seeds two empty rows by default; users may add more
        // and never fill them. Empty rows (no title or no authors) should be
        // dropped silently rather than throwing.
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.BookInput.Title = "Two Stories";
        vm.CollectionWorks =
        [
            new() { Title = "First Story", Authors = ["Author A"] },
            new() { Title = "", Authors = ["Author B"] },
            new() { Title = "Second Story", Authors = ["Author B"] },
            new() { Title = "Third Story", Authors = [] },
        ];

        await vm.SaveAsync(new List<int>());

        using var db = _factory.CreateDbContext();
        var book = db.Books.Include(b => b.Works).Single();
        Assert.Equal(2, book.Works.Count);
        Assert.Contains(book.Works, w => w.Title == "First Story");
        Assert.Contains(book.Works, w => w.Title == "Second Story");
    }

    [Fact]
    public async Task SaveAsync_CollectionMode_AllRowsEmpty_Throws()
    {
        var vm = CreateVm();
        vm.IsCollection = true;
        vm.BookInput.Title = "Empty Collection";
        vm.CollectionWorks = [new(), new()];

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveAsync(new List<int>()));
    }

    [Fact]
    public async Task SaveAsync_NonCollection_NoContributors_ThrowsWithUserFacingMessage()
    {
        // Drew's 2026-05-12 testing-feedback bug repro: Enter on the ISBN
        // field submits the EditForm before lookup runs, which calls SaveAsync
        // on an empty/sparse form. The non-collection branch throws
        // InvalidOperationException with a user-facing message — the Add page
        // catch block relies on that exception type to distinguish
        // validation-style errors (show .Message) from genuine crashes
        // (log + generic message). Lock the contract. Post-2026-05-24 the
        // check is "at least one contributor of any role" so editor-only
        // Works save (see SaveAsync_NonCollection_EditorOnly_Saves below).
        var vm = CreateVm();
        vm.BookInput.Title = "Untitled";
        // No authors AND no contributors on WorkInput.

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => vm.SaveAsync(new List<int>()));
        Assert.Contains("contributor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_NonCollection_EditorOnly_Saves()
    {
        // Dictionary / Oxford Companion case — Work has an editor but no
        // author. Should save successfully; the resulting Work carries one
        // WorkAuthor row with Role=Editor and no Author-role row.
        var vm = CreateVm();
        vm.BookInput.Title = "Concise Oxford English Dictionary";
        vm.WorkInput.Title = "Concise Oxford English Dictionary";
        vm.WorkInput.Authors = [];
        vm.WorkInput.Contributors =
        [
            new ContributorEntry { Name = "Catherine Soanes", Role = AuthorRole.Editor },
        ];

        var bookId = await vm.SaveAsync(selectedGenreIds: []);

        Assert.NotNull(bookId);
        await using var verify = _factory.CreateDbContext();
        var book = await verify.Books
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstAsync(b => b.Id == bookId);
        var work = Assert.Single(book.Works);
        var sole = Assert.Single(work.WorkAuthors);
        Assert.Equal(AuthorRole.Editor, sole.Role);
        Assert.Equal("Catherine Soanes", sole.Author.Name);
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
        await vm.LookupAsync();

        Assert.Equal(MatchReason.AuthorHasMultipleBooks, vm.SeriesSuggestion!.Reason);

        await vm.AcceptSeriesSuggestion();
        // Accept is a no-op on a local-fallback reason — nothing chosen.
        Assert.Null(vm.AcceptedSeriesId);
        Assert.Null(vm.AcceptedSeriesName);
    }
}
