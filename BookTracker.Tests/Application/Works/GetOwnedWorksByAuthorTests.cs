using BookTracker.Application.Works;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests;

// Integration tests for the online layer's "which of this author's works do
// I own" match against the process-shared SQL Server container.
[Trait("Category", TestCategories.Integration)]
public class GetOwnedWorksByAuthorTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<IReadOnlyDictionary<string, int>> OwnedAsync(string author, params string[] titles) =>
        new GetOwnedWorksByAuthorHandler(_factory).HandleAsync(new GetOwnedWorksByAuthor(author, titles));

    private async Task<int> SeedBookAsync(string title, string author)
    {
        await using var db = _factory.CreateDbContext();
        // Author.Name is unique, so a second book by the same author reuses the row.
        var authorRow = await db.Authors.SingleOrDefaultAsync(a => a.Name == author)
            ?? Author.Create(author);
        var book = TestBooks.Book(
            title,
            editions: [new Edition { Copies = [new Copy { Condition = BookCondition.Good }] }]);
        book.AttachWork(new Work
        {
            Title = title,
            WorkAuthors = [new WorkAuthor { Author = authorRow, Order = 0, Role = AuthorRole.Author }],
        });
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    private async Task MakeAliasAsync(string alias, string canonical)
    {
        await using var db = _factory.CreateDbContext();
        var canonicalRow = await db.Authors.SingleAsync(a => a.Name == canonical);
        var aliasRow = await db.Authors.SingleAsync(a => a.Name == alias);
        aliasRow.MarkAliasOf(canonicalRow);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExactTitle_returnsTheBookId()
    {
        var bookId = await SeedBookAsync("Mort", "Terry Pratchett");

        var owned = await OwnedAsync("Terry Pratchett", "Mort");

        Assert.Equal(new Dictionary<string, int> { ["Mort"] = bookId }, owned);
    }

    [Fact]
    public async Task ArticleAndPunctuationDifferences_stillMatch_keyedByTheTitleAsGiven()
    {
        var bookId = await SeedBookAsync("The Colour of Magic", "Terry Pratchett");

        var owned = await OwnedAsync("terry pratchett", "Colour of Magic!");

        Assert.Equal(new Dictionary<string, int> { ["Colour of Magic!"] = bookId }, owned);
    }

    [Fact]
    public async Task WorkByAnAlias_isFoundUnderTheCanonicalName()
    {
        await SeedBookAsync("Carrie", "Stephen King");
        var bookId = await SeedBookAsync("The Running Man", "Richard Bachman");
        await MakeAliasAsync("Richard Bachman", "Stephen King");

        var owned = await OwnedAsync("Stephen King", "The Running Man");

        Assert.Equal(new Dictionary<string, int> { ["The Running Man"] = bookId }, owned);
    }

    [Fact]
    public async Task AliasName_findsTheCanonicalsWorks()
    {
        var bookId = await SeedBookAsync("Carrie", "Stephen King");
        await SeedBookAsync("The Running Man", "Richard Bachman");
        await MakeAliasAsync("Richard Bachman", "Stephen King");

        var owned = await OwnedAsync("Richard Bachman", "Carrie");

        Assert.Equal(new Dictionary<string, int> { ["Carrie"] = bookId }, owned);
    }

    [Fact]
    public async Task TitleNotOwned_isAbsent()
    {
        var bookId = await SeedBookAsync("Mort", "Terry Pratchett");

        var owned = await OwnedAsync("Terry Pratchett", "Mort", "Small Gods");

        Assert.Equal(new Dictionary<string, int> { ["Mort"] = bookId }, owned);
    }

    [Fact]
    public async Task SameTitleByAnotherAuthor_isAbsent()
    {
        await SeedBookAsync("Dune", "Frank Herbert");

        Assert.Empty(await OwnedAsync("Brian Herbert", "Dune"));
    }

    [Fact]
    public async Task SoftDeletedBook_isAbsent()
    {
        var bookId = await SeedBookAsync("Mort", "Terry Pratchett");
        await using (var db = _factory.CreateDbContext())
        {
            var book = await db.Books.Include(b => b.Editions).SingleAsync(b => b.Id == bookId);
            book.SoftDelete();
            await db.SaveChangesAsync();
        }

        Assert.Empty(await OwnedAsync("Terry Pratchett", "Mort"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankAuthor_isEmpty(string author)
    {
        await SeedBookAsync("Mort", "Terry Pratchett");

        Assert.Empty(await OwnedAsync(author, "Mort"));
    }
}
