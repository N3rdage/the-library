using BookTracker.Application.Works;
using BookTracker.Data.Models;
using BookTracker.Shared.Online;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests;

// Integration tests for the online layer's owned-work match against the
// process-shared SQL Server container.
[Trait("Category", TestCategories.Integration)]
public class FindOwnedWorkTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<OwnedWorkMatch?> FindAsync(string isbn, string? title, string? author) =>
        new FindOwnedWorkHandler(_factory).HandleAsync(new FindOwnedWork(isbn, title, author));

    private async Task<int> SeedBookAsync(string bookTitle, string workTitle, string author, string isbn)
    {
        await using var db = _factory.CreateDbContext();
        var book = new Book
        {
            Title = bookTitle,
            Editions = [new Edition { Isbn = isbn, Copies = [new Copy { Condition = BookCondition.Good }] }],
        };
        book.AttachWork(new Work
        {
            Title = workTitle,
            WorkAuthors = [new WorkAuthor { Author = new Author { Name = author }, Order = 0, Role = AuthorRole.Author }],
        });
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    [Fact]
    public async Task OwnedIsbn_isTheSameEdition()
    {
        var bookId = await SeedBookAsync("The Colour of Magic", "The Colour of Magic", "Terry Pratchett", "9780552124751");

        var match = await FindAsync("9780552124751", "Some Other Title", "Someone Else");

        Assert.Equal(new OwnedWorkMatch(bookId, "The Colour of Magic", SameEdition: true), match);
    }

    [Fact]
    public async Task OtherPrintingOfAnOwnedWork_isTheWorkButNotTheEdition()
    {
        await SeedBookAsync("Mort", "Mort", "Terry Pratchett", "9780552131063");
        var bookId = await SeedBookAsync("The Colour of Magic", "The Colour of Magic", "Terry Pratchett", "9780552124751");

        var match = await FindAsync("9780062225672", "The Colour of Magic", "Terry Pratchett");

        Assert.Equal(new OwnedWorkMatch(bookId, "The Colour of Magic", SameEdition: false), match);
    }

    [Fact]
    public async Task ArticleAndPunctuationDifferences_stillMatchTheWork()
    {
        var bookId = await SeedBookAsync("The Colour of Magic", "The Colour of Magic", "Terry Pratchett", "9780552124751");

        var match = await FindAsync("9780062225672", "Colour of Magic!", "Pratchett, Terry, 1948-2015");

        Assert.Equal(new OwnedWorkMatch(bookId, "The Colour of Magic", SameEdition: false), match);
    }

    [Fact]
    public async Task NoAuthor_narrowsOnTheTitleAndStillMatches()
    {
        var bookId = await SeedBookAsync("The Colour of Magic", "The Colour of Magic", "Terry Pratchett", "9780552124751");

        var match = await FindAsync("9780062225672", "Colour of Magic", null);

        Assert.Equal(new OwnedWorkMatch(bookId, "The Colour of Magic", SameEdition: false), match);
    }

    [Fact]
    public async Task UnrelatedTitleBySameAuthor_isNotOwned()
    {
        await SeedBookAsync("The Colour of Magic", "The Colour of Magic", "Terry Pratchett", "9780552124751");

        var match = await FindAsync("9780552134637", "Small Gods", "Terry Pratchett");

        Assert.Null(match);
    }

    [Fact]
    public async Task SameTitleByAnotherAuthor_isNotOwned()
    {
        await SeedBookAsync("Dune", "Dune", "Frank Herbert", "9780441172719");

        var match = await FindAsync("9780000000001", "Dune", "Someone Unrelated");

        Assert.Null(match);
    }

    [Fact]
    public async Task SoftDeletedBook_isNotOwned()
    {
        var bookId = await SeedBookAsync("The Colour of Magic", "The Colour of Magic", "Terry Pratchett", "9780552124751");
        await using (var db = _factory.CreateDbContext())
        {
            var book = await db.Books.Include(b => b.Editions).SingleAsync(b => b.Id == bookId);
            book.SoftDelete();
            await db.SaveChangesAsync();
        }

        Assert.Null(await FindAsync("9780552124751", "The Colour of Magic", "Terry Pratchett"));
        Assert.Null(await FindAsync("9780062225672", "The Colour of Magic", "Terry Pratchett"));
    }

    [Fact]
    public async Task BlankTitleAndUnknownIsbn_isNotOwned()
    {
        await SeedBookAsync("The Colour of Magic", "The Colour of Magic", "Terry Pratchett", "9780552124751");

        Assert.Null(await FindAsync("9780062225672", "  ", "Terry Pratchett"));
    }
}
