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
                works: [TestWorks.Work("The Hobbit", Author.Create("Tolkien"))],
                editions: [TestEditions.Edition(isbn: "9780345391803", copies: [TestEditions.Copy(condition: BookCondition.Good)])]);
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
                    TestEditions.Edition(
                        isbn: "055210617X",
                        format: BookFormat.MassMarketPaperback,
                        datePrinted: new DateOnly(1983, 1, 1),
                        datePrintedPrecision: DatePrecision.Year,
                        publisher: new Publisher { Name = "Pinnacle" },
                        copies: [TestEditions.Copy(condition: BookCondition.Good)]),
                ]);
            book.AttachWork(TestWorks.Work(
                "Funny Money",
                subtitle: "The Destroyer #54",
                authorships:
                [
                    TestWorks.Authorship(Author.Create("Richard Sapir")),
                    TestWorks.Authorship(Author.Create("Some Editor"), 0, AuthorRole.Editor),
                ]));
            book.AttachWork(TestWorks.Work("Second Story", Author.Create("Warren Murphy"), subtitle: "not the lead"));
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
                editions: [TestEditions.Edition(isbn: "9780000000002", coverUrl: "https://example.test/edition.jpg", copies: [TestEditions.Copy()])]));
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
                editions: [TestEditions.Edition(isbn: "9780345391803", copies: [TestEditions.Copy(condition: BookCondition.Good)])]));
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
                    TestEditions.Edition(isbn: "9780441172719", copies: [TestEditions.Copy(), TestEditions.Copy()]),
                    TestEditions.Edition(isbn: "9780340960196", copies: [TestEditions.Copy()]),
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
            var first = TestWorks.Work(
                "Relic",
                authorships:
                [
                    TestWorks.Authorship(child, 1),
                    TestWorks.Authorship(preston),
                    TestWorks.Authorship(editor, 0, AuthorRole.Editor),
                ]);
            var second = TestWorks.Work("Reliquary", child);
            var book = TestBooks.Book(
                "Relic & Reliquary",
                editions: [TestEditions.Edition(isbn: "9780000000001", copies: [TestEditions.Copy()])]);
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
            var work = TestWorks.Work(
                "Concise Oxford English Dictionary",
                authorships: [TestWorks.Authorship(Author.Create("Catherine Soanes"), 0, AuthorRole.Editor)]);
            var book = TestBooks.Book(
                "Concise Oxford English Dictionary",
                editions: [TestEditions.Edition(isbn: "9780199601080", copies: [TestEditions.Copy()])]);
            book.AttachWork(work);
            db.Books.Add(book);
            await db.SaveChangesAsync();
        }

        var match = await FindAsync("9780199601080");

        Assert.Equal("", match!.Authors);
    }
}
