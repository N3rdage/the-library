using BookTracker.Application.Books;
using BookTracker.Application.Works;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the Add page's CreateBook handler against the
// process-shared SQL Server container: what one save persists across Book,
// Work, Author, Edition, Copy, Publisher, Genre and Series, and the user-facing
// validation throws. The VM-side mapping is covered by BookAddViewModelTests.
[Trait("Category", TestCategories.Integration)]
public class CreateBookHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private static readonly SeriesChoice NoSeries = new(null, null, null);

    private static NewBook BookTitled(string title, BookStatus status = BookStatus.Unread) =>
        new(title, BookCategory.Fiction, status, 0, null, null);

    private static NewEdition EditionWith(string? isbn = null, BookFormat format = BookFormat.TradePaperback, int? editionNumber = null) =>
        new(isbn, format, editionNumber, null, DatePrecision.Day, null, null);

    private static SingleWork OneWork(
        string title, string[] authors, ContributorInput[]? contributors = null, int[]? genreIds = null,
        DateOnly? firstPublished = null, DatePrecision precision = DatePrecision.Day) =>
        new(title, null, firstPublished, precision, authors, contributors ?? [], genreIds ?? []);

    private static WorkRow NewRow(
        string? title, string[] authors, int[]? genreIds = null,
        DateOnly? firstPublished = null, DatePrecision precision = DatePrecision.Day) =>
        new(null, title, null, firstPublished, precision, authors, [], genreIds ?? []);

    private static WorkRow AttachRow(int workId, string title) =>
        new(workId, title, null, null, DatePrecision.Day, [], [], []);

    private static WorkCollection Collection(params WorkRow[] rows) => new(rows, false, false, [], []);

    private Task<int> CreateAsync(
        NewBook book, NewBookWorks works, NewEdition? edition = null, SeriesChoice? series = null, string[]? tags = null) =>
        new CreateBookHandler(_factory).HandleAsync(
            new CreateBook(book, works, edition ?? EditionWith(), BookCondition.Good, series ?? NoSeries, tags ?? []));

    private async Task<int> SeedSeriesAsync(string name)
    {
        await using var db = _factory.CreateDbContext();
        var series = TestSeries.Series(name, SeriesType.Series);
        db.Series.Add(series);
        await db.SaveChangesAsync();
        return series.Id;
    }

    private async Task<int> SeedWorkOnOtherBookAsync(string workTitle, string author)
    {
        await using var db = _factory.CreateDbContext();
        var existing = TestWorks.Work(workTitle, Author.Create(author));
        db.Books.Add(TestBooks.Book("Other Anthology", works: [existing]));
        await db.SaveChangesAsync();
        return existing.Id;
    }

    [Fact]
    public async Task EditionNumber_persistsOnEdition()
    {
        var bookId = await CreateAsync(BookTitled("Joy of Cooking"), OneWork("Joy of Cooking", ["Irma Rombauer"]),
            EditionWith(format: BookFormat.Hardcover, editionNumber: 3));

        await using var db = _factory.CreateDbContext();
        var edition = await db.Editions.SingleAsync(e => e.BookId == bookId);
        Assert.Equal(3, edition.EditionNumber);
        Assert.Equal(BookFormat.Hardcover, edition.Format);
    }

    [Fact]
    public async Task NullEditionNumber_leavesColumnNull()
    {
        var bookId = await CreateAsync(BookTitled("Foundation"), OneWork("Foundation", ["Isaac Asimov"]),
            EditionWith(format: BookFormat.MassMarketPaperback));

        await using var db = _factory.CreateDbContext();
        var edition = await db.Editions.SingleAsync(e => e.BookId == bookId);
        Assert.Null(edition.EditionNumber);
    }

    [Fact]
    public async Task SingleAuthorCollection_appliesSharedAuthorsToEveryWork()
    {
        var works = new WorkCollection(
            [
                NewRow("Rita Hayworth and Shawshank Redemption", []),
                NewRow("Apt Pupil", []),
                NewRow("The Body", []),
                NewRow("The Breathing Method", []),
            ],
            SingleAuthor: true, SingleGenre: false, SharedAuthors: ["Stephen King"], SharedGenreIds: []);

        var bookId = await CreateAsync(BookTitled("Different Seasons"), works);

        await using var db = _factory.CreateDbContext();
        var saved = await db.Books
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstAsync(b => b.Id == bookId);
        Assert.Equal(4, saved.Works.Count);
        Assert.All(saved.Works, w =>
        {
            Assert.Single(w.WorkAuthors);
            Assert.Equal("Stephen King", w.WorkAuthors.Single().Author.Name);
        });
        // One shared Author row, not one per work.
        Assert.Equal(1, await db.Authors.CountAsync(a => a.Name == "Stephen King"));
    }

    [Fact]
    public async Task Collection_attachesBookLevelSeries()
    {
        // Series membership is a per-Book concept, so a collection carries it
        // just like a single-work book.
        var seriesId = await SeedSeriesAsync("The Sandman");
        var works = new WorkCollection([NewRow("Chapter 1", []), NewRow("Chapter 2", [])],
            SingleAuthor: true, SingleGenre: false, SharedAuthors: ["Neil Gaiman"], SharedGenreIds: []);

        var bookId = await CreateAsync(BookTitled("The Sandman: Season of Mists"), works,
            series: new SeriesChoice(seriesId, "The Sandman", "4"));

        await using var db = _factory.CreateDbContext();
        var saved = await db.Books.Include(b => b.Series).FirstAsync(b => b.Id == bookId);
        Assert.NotNull(saved.Series);
        Assert.Equal("The Sandman", saved.Series!.Name);
        Assert.Equal(4, saved.SeriesOrder);
    }

    [Fact]
    public async Task SingleAuthorCollection_noSharedAuthors_throwsAndSavesNothing()
    {
        // Single-Author mode with no shared authors fails with a clear message
        // rather than silently producing zero works.
        var works = new WorkCollection([NewRow("Story A", [])],
            SingleAuthor: true, SingleGenre: false, SharedAuthors: [], SharedGenreIds: []);

        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => CreateAsync(BookTitled("Untitled Collection"), works));

        Assert.Contains("Single-Author", ex.Message);
        await using var db = _factory.CreateDbContext();
        Assert.Empty(db.Books);
    }

    [Fact]
    public async Task SingleGenreCollection_appliesSharedGenresToEveryWork()
    {
        int sfId;
        await using (var db = _factory.CreateDbContext())
        {
            var sf = new Genre { Name = "Science Fiction" };
            db.Genres.Add(sf);
            await db.SaveChangesAsync();
            sfId = sf.Id;
        }
        var works = new WorkCollection(
            [NewRow("Story A", []), NewRow("Story B", []), NewRow("Story C", [])],
            SingleAuthor: true, SingleGenre: true, SharedAuthors: ["Various"], SharedGenreIds: [sfId]);

        var bookId = await CreateAsync(BookTitled("The Mammoth Book of SF"), works);

        await using var db2 = _factory.CreateDbContext();
        var saved = await db2.Books
            .Include(b => b.Works).ThenInclude(w => w.Genres)
            .FirstAsync(b => b.Id == bookId);
        Assert.Equal(3, saved.Works.Count);
        Assert.All(saved.Works, w => Assert.Equal(sfId, Assert.Single(w.Genres).Id));
    }

    [Fact]
    public async Task PerRowGenres_attachEachWorksOwnList()
    {
        int sfId, horrorId;
        await using (var db = _factory.CreateDbContext())
        {
            var sf = new Genre { Name = "Science Fiction" };
            var horror = new Genre { Name = "Horror" };
            db.Genres.AddRange(sf, horror);
            await db.SaveChangesAsync();
            sfId = sf.Id;
            horrorId = horror.Id;
        }

        var bookId = await CreateAsync(BookTitled("Mostly SF, One Horror"), Collection(
            NewRow("SF One", ["Author A"], [sfId]),
            NewRow("SF Two", ["Author A"], [sfId]),
            NewRow("Horror One", ["Author A"], [horrorId])));

        await using var db2 = _factory.CreateDbContext();
        var saved = await db2.Books
            .Include(b => b.Works).ThenInclude(w => w.Genres)
            .FirstAsync(b => b.Id == bookId);
        Assert.Equal(3, saved.Works.Count);
        Assert.Equal(sfId, Assert.Single(saved.Works.Single(w => w.Title == "SF One").Genres).Id);
        Assert.Equal(horrorId, Assert.Single(saved.Works.Single(w => w.Title == "Horror One").Genres).Id);
    }

    [Fact]
    public async Task SingleGenreCollection_emptySharedList_leavesWorksGenreless()
    {
        var works = new WorkCollection([NewRow("Story A", []), NewRow("Story B", [])],
            SingleAuthor: true, SingleGenre: true, SharedAuthors: ["Various"], SharedGenreIds: []);

        var bookId = await CreateAsync(BookTitled("Untagged Collection"), works);

        await using var db = _factory.CreateDbContext();
        var saved = await db.Books
            .Include(b => b.Works).ThenInclude(w => w.Genres)
            .FirstAsync(b => b.Id == bookId);
        Assert.Equal(2, saved.Works.Count);
        Assert.All(saved.Works, w => Assert.Empty(w.Genres));
    }

    [Fact]
    public async Task SingleWork_attachesTheSelectedGenres()
    {
        int sfId;
        await using (var db = _factory.CreateDbContext())
        {
            var sf = new Genre { Name = "Science Fiction" };
            db.Genres.Add(sf);
            await db.SaveChangesAsync();
            sfId = sf.Id;
        }

        var bookId = await CreateAsync(BookTitled("Dune"), OneWork("Dune", ["Frank Herbert"], genreIds: [sfId]));

        await using var db2 = _factory.CreateDbContext();
        var work = await db2.Works.Include(w => w.Genres).SingleAsync();
        Assert.Equal(sfId, Assert.Single(work.Genres).Id);
    }

    [Fact]
    public async Task BlankIsbn_persistsAsNull()
    {
        await CreateAsync(BookTitled("And Then There Were None"), OneWork("And Then There Were None", ["Agatha Christie"]),
            EditionWith(isbn: ""));

        await using var db = _factory.CreateDbContext();
        Assert.Null(db.Editions.Single().Isbn);
    }

    [Fact]
    public async Task PaddedIsbn_isStoredTrimmedOnly()
    {
        // The ISBN is trimmed, not normalised: separators survive.
        await CreateAsync(BookTitled("And Then There Were None"), OneWork("And Then There Were None", ["Agatha Christie"]),
            EditionWith(isbn: " 978-0-00-713683-4 "));

        await using var db = _factory.CreateDbContext();
        Assert.Equal("978-0-00-713683-4", db.Editions.Single().Isbn);
    }

    [Fact]
    public async Task TwoNoIsbnEditions_coexistWithoutCollision()
    {
        // The filtered unique index permits multiple null ISBNs; an unfiltered
        // one would throw on the second save.
        await CreateAsync(BookTitled("Book A"), OneWork("Book A", ["Author A"]));
        await CreateAsync(BookTitled("Book B"), OneWork("Book B", ["Author B"]));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, db.Editions.Count(e => e.Isbn == null));
    }

    [Fact]
    public async Task SingleWork_createsBookAndWorkTogether()
    {
        var bookId = await CreateAsync(BookTitled("The Hobbit"),
            OneWork("The Hobbit", ["J.R.R. Tolkien"], firstPublished: new DateOnly(1937, 9, 21)),
            EditionWith(isbn: "9780345391803"));

        await using var db = _factory.CreateDbContext();
        var book = db.Books
            .Include(b => b.BookWorks).ThenInclude(bw => bw.Work).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Single();
        Assert.Equal(bookId, book.Id);
        var link = Assert.Single(book.BookWorks);
        Assert.Equal(0, link.Order);
        var work = link.Work;
        Assert.Equal("The Hobbit", work.Title);
        Assert.Equal("J.R.R. Tolkien", work.WorkAuthors.OrderBy(wa => wa.Order).First().Author.Name);
        Assert.Equal(new DateOnly(1937, 9, 21), work.FirstPublishedDate);
        Assert.Equal(DatePrecision.Day, work.FirstPublishedDatePrecision);
        Assert.Equal("9780345391803", db.Editions.Single().Isbn);
    }

    [Fact]
    public async Task PersistsBookFieldsEditionCopyAndPublisher()
    {
        var command = new CreateBook(
            new NewBook("  Mort  ", BookCategory.NonFiction, BookStatus.Read, 4, "  signed  ", "  https://covers.example.org/mort.jpg  "),
            OneWork("Mort", ["Terry Pratchett"]),
            new NewEdition(null, BookFormat.Hardcover, null, new DateOnly(1987, 10, 1), DatePrecision.Month,
                "  Gollancz  ", "  https://covers.example.org/mort-ed.jpg  "),
            BookCondition.Fair,
            NoSeries,
            []);

        var bookId = await new CreateBookHandler(_factory).HandleAsync(command);

        await using var db = _factory.CreateDbContext();
        var book = await db.Books
            .Include(b => b.Editions).ThenInclude(e => e.Copies)
            .Include(b => b.Editions).ThenInclude(e => e.Publisher)
            .SingleAsync(b => b.Id == bookId);
        Assert.Equal("Mort", book.Title);
        Assert.Equal(BookCategory.NonFiction, book.Category);
        Assert.Equal(BookStatus.Read, book.Status);
        Assert.Equal(4, book.Rating);
        Assert.Equal("signed", book.Notes);
        Assert.Equal("https://covers.example.org/mort.jpg", book.DefaultCoverArtUrl);
        var edition = Assert.Single(book.Editions);
        Assert.Equal(new DateOnly(1987, 10, 1), edition.DatePrinted);
        Assert.Equal(DatePrecision.Month, edition.DatePrintedPrecision);
        Assert.Equal("https://covers.example.org/mort-ed.jpg", edition.CoverUrl);
        Assert.Equal("Gollancz", edition.Publisher!.Name);
        Assert.Equal(BookCondition.Fair, Assert.Single(edition.Copies).Condition);
    }

    [Fact]
    public async Task DefaultUnreadStatus_persists()
    {
        await CreateAsync(BookTitled("The Hobbit", BookStatus.Unread), OneWork("The Hobbit", ["J.R.R. Tolkien"]));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(BookStatus.Unread, db.Books.Single().Status);
    }

    [Fact]
    public async Task MultipleAuthors_writeTheJoinInChipOrder()
    {
        await CreateAsync(BookTitled("Relic"), OneWork("Relic", ["Douglas Preston", "Lincoln Child"]),
            EditionWith(isbn: "9780812543261"));

        await using var db = _factory.CreateDbContext();
        var work = db.Works.Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author).Single();
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
    public async Task ExistingSeriesById_attachesWithOrder_andCreatesNoSeries()
    {
        var seriesId = await SeedSeriesAsync("Discworld");

        await CreateAsync(BookTitled("Sourcery"), OneWork("Sourcery", ["Terry Pratchett"]),
            series: new SeriesChoice(seriesId, "Discworld", "5"));

        await using var db = _factory.CreateDbContext();
        var book = db.Books.Single();
        Assert.Equal(seriesId, book.SeriesId);
        Assert.Equal(5, book.SeriesOrder);
        Assert.Equal(1, db.Series.Count());
    }

    [Fact]
    public async Task SeriesByName_findOrCreatesASeriesTypeRow_andAttaches()
    {
        // No id (an eager create was skipped or failed): the save's
        // find-or-create net makes the row.
        await CreateAsync(BookTitled("The Way of Kings"), OneWork("The Way of Kings", ["Brandon Sanderson"]),
            series: new SeriesChoice(null, "The Stormlight Archive", "1"));

        await using var db = _factory.CreateDbContext();
        var series = Assert.Single(db.Series);
        Assert.Equal("The Stormlight Archive", series.Name);
        Assert.Equal(SeriesType.Series, series.Type); // Q1 default — not Collection.
        var book = db.Books.Single();
        Assert.Equal(series.Id, book.SeriesId);
        Assert.Equal(1, book.SeriesOrder);
    }

    [Fact]
    public async Task NoSeries_attachesNothing()
    {
        await CreateAsync(BookTitled("The Way of Kings"), OneWork("The Way of Kings", ["Brandon Sanderson"]));

        await using var db = _factory.CreateDbContext();
        Assert.Empty(db.Series);
        var book = db.Books.Single();
        Assert.Null(book.SeriesId);
        Assert.Null(book.SeriesOrder);
    }

    [Fact]
    public async Task SeriesWithNonIntegerOrder_persistsFlooredOrderAndDisplay()
    {
        var seriesId = await SeedSeriesAsync("The Stormlight Archive");

        await CreateAsync(BookTitled("Edgedancer"), OneWork("Edgedancer", ["Brandon Sanderson"]),
            series: new SeriesChoice(seriesId, "The Stormlight Archive", "2.5"));

        await using var db = _factory.CreateDbContext();
        var book = db.Books.Single();
        Assert.Equal(seriesId, book.SeriesId);
        Assert.Equal(2, book.SeriesOrder);             // floored sort key
        Assert.Equal("2.5", book.SeriesOrderDisplay);  // raw display override
    }

    [Fact]
    public async Task Tags_reuseAnExistingTag_inTheSameSaveAsTheBook()
    {
        // Bulk add's follow-up: the seeded tag is attached, not duplicated.
        await using (var seed = _factory.CreateDbContext())
        {
            seed.Tags.Add(new Tag { Name = "follow-up" });
            await seed.SaveChangesAsync();
        }

        var bookId = await CreateAsync(BookTitled("The Hobbit"), OneWork("The Hobbit", ["J.R.R. Tolkien"]),
            tags: ["follow-up"]);

        await using var db = _factory.CreateDbContext();
        var book = await db.Books.Include(b => b.Tags).SingleAsync(b => b.Id == bookId);
        Assert.Equal("follow-up", Assert.Single(book.Tags).Name);
        Assert.Equal(1, await db.Tags.CountAsync());
    }

    [Fact]
    public async Task Tags_createAMissingTag_normalisedAndOnce()
    {
        var bookId = await CreateAsync(BookTitled("The Hobbit"), OneWork("The Hobbit", ["J.R.R. Tolkien"]),
            tags: ["  Follow-Up ", "follow-up", " "]);

        await using var db = _factory.CreateDbContext();
        var book = await db.Books.Include(b => b.Tags).SingleAsync(b => b.Id == bookId);
        Assert.Equal("follow-up", Assert.Single(book.Tags).Name);
        Assert.Equal(1, await db.Tags.CountAsync());
    }

    [Fact]
    public async Task NoTags_attachesNone()
    {
        var bookId = await CreateAsync(BookTitled("The Hobbit"), OneWork("The Hobbit", ["J.R.R. Tolkien"]));

        await using var db = _factory.CreateDbContext();
        Assert.Empty((await db.Books.Include(b => b.Tags).SingleAsync(b => b.Id == bookId)).Tags);
        Assert.Empty(db.Tags);
    }

    [Fact]
    public async Task SingleWork_genreIds_attachToTheWork()
    {
        int fantasyId, highFantasyId;
        await using (var seed = _factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "Fantasy" };
            var high = new Genre { Name = "High Fantasy", ParentGenre = fantasy };
            seed.Genres.AddRange(fantasy, high);
            await seed.SaveChangesAsync();
            fantasyId = fantasy.Id;
            highFantasyId = high.Id;
        }

        await CreateAsync(BookTitled("The Hobbit"), OneWork("The Hobbit", ["J.R.R. Tolkien"], genreIds: [highFantasyId, fantasyId]));

        await using var db = _factory.CreateDbContext();
        var work = await db.Works.Include(w => w.Genres).SingleAsync();
        Assert.Equal([fantasyId, highFantasyId], work.Genres.Select(g => g.Id).Order());
    }

    [Fact]
    public async Task SeriesWithoutOrder_attachesWithNullOrder()
    {
        // The carryover guard's persistence half: a series chosen after the
        // order was dropped attaches with no order.
        var seriesId = await SeedSeriesAsync("Discworld");

        await CreateAsync(BookTitled("The Way of Kings"), OneWork("The Way of Kings", ["Brandon Sanderson"]),
            series: new SeriesChoice(seriesId, "Discworld", null));

        await using var db = _factory.CreateDbContext();
        var book = db.Books.Single();
        Assert.Equal(seriesId, book.SeriesId);
        Assert.Null(book.SeriesOrder);
    }

    [Fact]
    public async Task Collection_mixedNewAndAttachedRows_attachesExistingAndCreatesNew_inRowOrder()
    {
        // Drew's Lovecraft "complete works" case: some stories are new, some are
        // already in the library from previous anthologies.
        var existingId = await SeedWorkOnOtherBookAsync("The Call of Cthulhu", "H.P. Lovecraft");

        var bookId = await CreateAsync(BookTitled("Mixed Anthology"), Collection(
            NewRow("New Story A", ["H.P. Lovecraft"]),
            AttachRow(existingId, "The Call of Cthulhu"),
            NewRow("New Story B", ["H.P. Lovecraft"])));

        await using var db = _factory.CreateDbContext();
        var book = await db.Books
            .Include(b => b.BookWorks).ThenInclude(bw => bw.Work)
            .FirstAsync(b => b.Id == bookId);
        Assert.Equal(
            ["New Story A", "The Call of Cthulhu", "New Story B"],
            book.BookWorks.OrderBy(bw => bw.Order).Select(bw => bw.Work.Title));
        Assert.Equal([0, 1, 2], book.BookWorks.Select(bw => bw.Order).Order());
        Assert.Contains(book.BookWorks, bw => bw.WorkId == existingId);
        // Existing Work still has its original anthology attached too — N:N preserved.
        var cthulhu = await db.Works.Include(w => w.Books).FirstAsync(w => w.Id == existingId);
        Assert.Equal(2, cthulhu.Books.Count);
        // The new rows reuse the existing Author row.
        Assert.Equal(1, await db.Authors.CountAsync(a => a.Name == "H.P. Lovecraft"));
    }

    [Fact]
    public async Task Collection_attachOnlyRow_doesNotRequireTitleOrAuthorOnThatRow()
    {
        var existingId = await SeedWorkOnOtherBookAsync("Existing", "Author");

        var bookId = await CreateAsync(BookTitled("Attach-only book"), Collection(AttachRow(existingId, "Existing")));

        await using var db = _factory.CreateDbContext();
        var book = db.Books.Include(b => b.Works).Single(b => b.Id == bookId);
        Assert.Equal(existingId, Assert.Single(book.Works).Id);
    }

    [Fact]
    public async Task Collection_attachRowForADeletedWork_isDropped()
    {
        // A work deleted between the pick and the save doesn't fail the save.
        var bookId = await CreateAsync(BookTitled("Two Stories"), Collection(
            NewRow("Kept", ["Author A"]),
            AttachRow(424242, "Gone")));

        await using var db = _factory.CreateDbContext();
        var book = db.Books.Include(b => b.Works).Single(b => b.Id == bookId);
        Assert.Equal("Kept", Assert.Single(book.Works).Title);
    }

    [Fact]
    public async Task Collection_createsBookWithMultipleWorks()
    {
        await CreateAsync(BookTitled("The Bachman Books"), Collection(
                NewRow("Rage", ["Richard Bachman"]),
                NewRow("The Long Walk", ["Richard Bachman"]),
                NewRow("Roadwork", ["Richard Bachman"], firstPublished: new DateOnly(1981, 1, 1), precision: DatePrecision.Year)),
            EditionWith(isbn: "9780451178121"));

        await using var db = _factory.CreateDbContext();
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
    public async Task Collection_skipsEmptyRows()
    {
        // Rows with no title or no author entries are dropped silently.
        await CreateAsync(BookTitled("Two Stories"), Collection(
            NewRow("First Story", ["Author A"]),
            NewRow("", ["Author B"]),
            NewRow("Second Story", ["Author B"]),
            NewRow("Third Story", [])));

        await using var db = _factory.CreateDbContext();
        var book = db.Books.Include(b => b.Works).Single();
        Assert.Equal(2, book.Works.Count);
        Assert.Contains(book.Works, w => w.Title == "First Story");
        Assert.Contains(book.Works, w => w.Title == "Second Story");
    }

    [Fact]
    public async Task Collection_allRowsEmpty_throws()
    {
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() =>
            CreateAsync(BookTitled("Empty Collection"), Collection(NewRow(null, []), NewRow(null, []))));

        Assert.Contains("at least one work", ex.Message);
        await using var db = _factory.CreateDbContext();
        Assert.Empty(db.Books);
    }

    [Fact]
    public async Task Collection_rowWithOnlyBlankAuthorEntries_throwsNamingTheRow()
    {
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() =>
            CreateAsync(BookTitled("Anthology"), Collection(
                NewRow("Fine Story", ["Author A"]),
                NewRow("Odd Story", ["   "]))));

        Assert.Equal("Each work in the collection needs at least one contributor (work \"Odd Story\" had none).", ex.Message);
        await using var db = _factory.CreateDbContext();
        Assert.Empty(db.Books);
    }

    [Fact]
    public async Task SingleWork_noContributors_throwsWithUserFacingMessage()
    {
        // The Add page shows a DomainRuleException's message as-is
        // (e.g. Enter on the ISBN field submitting a sparse form). Post-2026-05-24
        // the check is "at least one contributor of any role", so editor-only
        // Works save.
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() =>
            CreateAsync(BookTitled("Untitled"), OneWork("Untitled", [])));

        Assert.Contains("contributor", ex.Message, StringComparison.OrdinalIgnoreCase);
        await using var db = _factory.CreateDbContext();
        Assert.Empty(db.Books);
    }

    [Fact]
    public async Task SingleWork_editorOnly_saves()
    {
        // Dictionary / Oxford Companion case — one WorkAuthor row with
        // Role=Editor and no Author-role row.
        var bookId = await CreateAsync(BookTitled("Concise Oxford English Dictionary"),
            OneWork("Concise Oxford English Dictionary", [],
                contributors: [new ContributorInput("Catherine Soanes", AuthorRole.Editor)]));

        await using var db = _factory.CreateDbContext();
        var book = await db.Books
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstAsync(b => b.Id == bookId);
        var sole = Assert.Single(Assert.Single(book.Works).WorkAuthors);
        Assert.Equal(AuthorRole.Editor, sole.Role);
        Assert.Equal("Catherine Soanes", sole.Author.Name);
    }
}
