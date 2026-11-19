using BookTracker.Application.Books;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the Add page's existing-edition read against the
// process-shared SQL Server container.
[Trait("Category", TestCategories.Integration)]
public class FindEditionByIsbnHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<ExistingBookMatch?> FindAsync(string isbn) =>
        new FindEditionByIsbnHandler(_factory).HandleAsync(new FindEditionByIsbn(isbn));

    [Fact]
    public async Task ExistingIsbn_returnsTheBookEditionTitleAuthorsAndCopyCount()
    {
        int bookId, editionId;
        await using (var db = _factory.CreateDbContext())
        {
            var book = TestBooks.Book(
                "The Hobbit",
                works: [new Work { Title = "The Hobbit", WorkAuthors = [new WorkAuthor { Author = Author.Create("Tolkien"), Order = 0 }] }],
                editions: [new Edition { Isbn = "9780345391803", Copies = [new Copy { Condition = BookCondition.Good }] }]);
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            editionId = book.Editions[0].Id;
        }

        var match = await FindAsync("9780345391803");

        Assert.Equal(
            new ExistingBookMatch(bookId, editionId, "The Hobbit", "Tolkien", 1,
                null, "Tolkien", null, null, null, DatePrecision.Day, BookFormat.TradePaperback),
            match);
    }

    [Fact]
    public async Task ExistingIsbn_returnsTheLeadWorkEditionDetailsAndBookCoverFallback()
    {
        // Bulk add hydrates an owned row from these (055210617X, Funny Money by
        // Richard Sapir: an ISBN the upstream providers do not index).
        await using (var db = _factory.CreateDbContext())
        {
            var book = TestBooks.Book(
                "Funny Money",
                coverUrl: "https://example.test/funny-money.jpg",
                editions:
                [
                    new Edition
                    {
                        Isbn = "055210617X",
                        Format = BookFormat.MassMarketPaperback,
                        DatePrinted = new DateOnly(1983, 1, 1),
                        DatePrintedPrecision = DatePrecision.Year,
                        Publisher = new Publisher { Name = "Pinnacle" },
                        Copies = [new Copy { Condition = BookCondition.Good }],
                    },
                ]);
            book.AttachWork(new Work
            {
                Title = "Funny Money",
                Subtitle = "The Destroyer #54",
                WorkAuthors =
                [
                    new WorkAuthor { Author = Author.Create("Richard Sapir"), Order = 0 },
                    new WorkAuthor { Author = Author.Create("Some Editor"), Order = 0, Role = AuthorRole.Editor },
                ],
            });
            book.AttachWork(new Work
            {
                Title = "Second Story",
                Subtitle = "not the lead",
                WorkAuthors = [new WorkAuthor { Author = Author.Create("Warren Murphy"), Order = 0 }],
            });
            db.Books.Add(book);
            await db.SaveChangesAsync();
        }

        var match = await FindAsync("055210617X");

        Assert.NotNull(match);
        Assert.Equal("Funny Money", match.Title);
        Assert.Equal("The Destroyer #54", match.LeadWorkSubtitle);
        Assert.Equal("Richard Sapir; Some Editor (editor)", match.LeadWorkAuthorship);
        Assert.Equal("https://example.test/funny-money.jpg", match.CoverUrl);
        Assert.Equal("Pinnacle", match.Publisher);
        Assert.Equal(new DateOnly(1983, 1, 1), match.DatePrinted);
        Assert.Equal(DatePrecision.Year, match.DatePrintedPrecision);
        Assert.Equal(BookFormat.MassMarketPaperback, match.Format);
    }

    [Fact]
    public async Task EditionCover_winsOverTheBookDefault_andABookWithoutWorksHasNoLeadWork()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Books.Add(TestBooks.Book(
                "Loose Edition",
                coverUrl: "https://example.test/book.jpg",
                editions: [new Edition { Isbn = "9780000000002", CoverUrl = "https://example.test/edition.jpg", Copies = [new Copy()] }]));
            await db.SaveChangesAsync();
        }

        var match = await FindAsync("9780000000002");

        Assert.NotNull(match);
        Assert.Equal("https://example.test/edition.jpg", match.CoverUrl);
        Assert.Null(match.LeadWorkSubtitle);
        Assert.Null(match.LeadWorkAuthorship);
        Assert.Null(match.Publisher);
    }

    [Fact]
    public async Task UnknownIsbn_returnsNull()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Books.Add(TestBooks.Book(
                "The Hobbit",
                editions: [new Edition { Isbn = "9780345391803", Copies = [new Copy { Condition = BookCondition.Good }] }]));
            await db.SaveChangesAsync();
        }

        Assert.Null(await FindAsync("9780000000000"));
    }

    [Fact]
    public async Task CopyCount_countsOnlyTheMatchedEditionsCopies()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Books.Add(TestBooks.Book(
                "Dune",
                editions:
                [
                    new Edition { Isbn = "9780441172719", Copies = [new Copy(), new Copy()] },
                    new Edition { Isbn = "9780340960196", Copies = [new Copy()] },
                ]));
            await db.SaveChangesAsync();
        }

        var match = await FindAsync("9780441172719");

        Assert.Equal(2, match!.CopyCount);
    }

    [Fact]
    public async Task Authors_joinAuthorRoleNamesInWorkThenLeadOrder_onceEach()
    {
        // Lead author first within each work, works in the book's order,
        // editors and other roles left out, a name shared by two works shown
        // once.
        await using (var db = _factory.CreateDbContext())
        {
            var preston = Author.Create("Douglas Preston");
            var child = Author.Create("Lincoln Child");
            var editor = Author.Create("Some Editor");
            var first = new Work
            {
                Title = "Relic",
                WorkAuthors =
                [
                    new WorkAuthor { Author = child, Order = 1, Role = AuthorRole.Author },
                    new WorkAuthor { Author = preston, Order = 0, Role = AuthorRole.Author },
                    new WorkAuthor { Author = editor, Order = 0, Role = AuthorRole.Editor },
                ],
            };
            var second = new Work
            {
                Title = "Reliquary",
                WorkAuthors = [new WorkAuthor { Author = child, Order = 0, Role = AuthorRole.Author }],
            };
            var book = TestBooks.Book(
                "Relic & Reliquary",
                editions: [new Edition { Isbn = "9780000000001", Copies = [new Copy()] }]);
            book.AttachWork(first);
            book.AttachWork(second);
            db.Books.Add(book);
            await db.SaveChangesAsync();
        }

        var match = await FindAsync("9780000000001");

        Assert.Equal("Douglas Preston, Lincoln Child", match!.Authors);
    }

    [Fact]
    public async Task EditorOnlyBook_returnsEmptyAuthors()
    {
        await using (var db = _factory.CreateDbContext())
        {
            var work = new Work
            {
                Title = "Concise Oxford English Dictionary",
                WorkAuthors = [new WorkAuthor { Author = Author.Create("Catherine Soanes"), Order = 0, Role = AuthorRole.Editor }],
            };
            var book = TestBooks.Book(
                "Concise Oxford English Dictionary",
                editions: [new Edition { Isbn = "9780199601080", Copies = [new Copy()] }]);
            book.AttachWork(work);
            db.Books.Add(book);
            await db.SaveChangesAsync();
        }

        var match = await FindAsync("9780199601080");

        Assert.Equal("", match!.Authors);
    }
}
