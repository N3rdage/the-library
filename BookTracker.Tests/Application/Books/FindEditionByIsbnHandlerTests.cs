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
            var book = new Book
            {
                Title = "The Hobbit",
                Works = [new Work { Title = "The Hobbit", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "Tolkien" }, Order = 0 }] }],
                Editions = [new Edition { Isbn = "9780345391803", Copies = [new Copy { Condition = BookCondition.Good }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            editionId = book.Editions[0].Id;
        }

        var match = await FindAsync("9780345391803");

        Assert.Equal(new ExistingBookMatch(bookId, editionId, "The Hobbit", "Tolkien", 1), match);
    }

    [Fact]
    public async Task UnknownIsbn_returnsNull()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Books.Add(new Book
            {
                Title = "The Hobbit",
                Editions = [new Edition { Isbn = "9780345391803", Copies = [new Copy { Condition = BookCondition.Good }] }],
            });
            await db.SaveChangesAsync();
        }

        Assert.Null(await FindAsync("9780000000000"));
    }

    [Fact]
    public async Task CopyCount_countsOnlyTheMatchedEditionsCopies()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Books.Add(new Book
            {
                Title = "Dune",
                Editions =
                [
                    new Edition { Isbn = "9780441172719", Copies = [new Copy(), new Copy()] },
                    new Edition { Isbn = "9780340960196", Copies = [new Copy()] },
                ],
            });
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
            var preston = new Author { Name = "Douglas Preston" };
            var child = new Author { Name = "Lincoln Child" };
            var editor = new Author { Name = "Some Editor" };
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
            var book = new Book
            {
                Title = "Relic & Reliquary",
                Editions = [new Edition { Isbn = "9780000000001", Copies = [new Copy()] }],
            };
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
                WorkAuthors = [new WorkAuthor { Author = new Author { Name = "Catherine Soanes" }, Order = 0, Role = AuthorRole.Editor }],
            };
            var book = new Book
            {
                Title = "Concise Oxford English Dictionary",
                Editions = [new Edition { Isbn = "9780199601080", Copies = [new Copy()] }],
            };
            book.AttachWork(work);
            db.Books.Add(book);
            await db.SaveChangesAsync();
        }

        var match = await FindAsync("9780199601080");

        Assert.Equal("", match!.Authors);
    }
}
