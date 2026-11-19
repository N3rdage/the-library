using BookTracker.Application.Books;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for bulk add's existing-edition write against the
// process-shared SQL Server container: a scanned ISBN already in the library
// gains a Copy (and any tags) in one save.
[Trait("Category", TestCategories.Integration)]
public class AddScannedCopyHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<int?> ScanAsync(string isbn, params string[] tags) =>
        new AddScannedCopyHandler(_factory).HandleAsync(new AddScannedCopy(isbn, tags));

    private async Task<int> SeedHobbitAsync(params string[] tags)
    {
        await using var db = _factory.CreateDbContext();
        var book = TestBooks.Book(
            "The Hobbit",
            tags: tags.Select(t => new Tag { Name = t }),
            editions: [TestEditions.Edition(isbn: "9780345391803", copies: [TestEditions.Copy(condition: BookCondition.Good)])]);
        book.AttachWork(new Work
        {
            Title = "The Hobbit",
            WorkAuthors = [new WorkAuthor { Author = Author.Create("J.R.R. Tolkien"), Order = 0 }],
        });
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    [Fact]
    public async Task ExistingIsbn_addsAGoodCopyToThatEdition_notANewBook()
    {
        await SeedHobbitAsync();

        var copyId = await ScanAsync("9780345391803");

        await using var db = _factory.CreateDbContext();
        Assert.Single(db.Books);    // Only one Book row…
        Assert.Single(db.Editions); // …and one Edition…
        var copies = await db.Copies.ToListAsync();
        Assert.Equal(2, copies.Count); // …with two Copies.
        Assert.Contains(copies, c => c.Id == copyId && c.Condition == BookCondition.Good);
        Assert.Empty(db.Tags);
    }

    [Fact]
    public async Task UnknownIsbn_returnsNull_andWritesNothing()
    {
        await SeedHobbitAsync();

        Assert.Null(await ScanAsync("9780000000000", "follow-up"));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(1, await db.Copies.CountAsync());
        Assert.Empty(db.Tags);
    }

    [Fact]
    public async Task Tags_areFoundOrCreated_andAttachedToTheBook()
    {
        var bookId = await SeedHobbitAsync();

        await ScanAsync("9780345391803", "follow-up");

        await using var db = _factory.CreateDbContext();
        var book = await db.Books.Include(b => b.Tags).SingleAsync(b => b.Id == bookId);
        Assert.Equal("follow-up", Assert.Single(book.Tags).Name);
        Assert.Equal(2, await db.Copies.CountAsync());
    }

    [Fact]
    public async Task TagAlreadyOnTheBook_isNotAddedTwice()
    {
        var bookId = await SeedHobbitAsync("follow-up");

        await ScanAsync("9780345391803", "follow-up");

        await using var db = _factory.CreateDbContext();
        var book = await db.Books.Include(b => b.Tags).SingleAsync(b => b.Id == bookId);
        Assert.Single(book.Tags);
        Assert.Equal(1, await db.Tags.CountAsync());
        Assert.Equal(2, await db.Copies.CountAsync());
    }
}
