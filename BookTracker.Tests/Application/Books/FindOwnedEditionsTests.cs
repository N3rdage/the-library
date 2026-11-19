using BookTracker.Application.Books;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests;

// Integration tests for the online layer's owned-edition match against the
// process-shared SQL Server container. The match is by ISBN alone, so the
// seeds are Books with Editions and no Works or Authors.
[Trait("Category", TestCategories.Integration)]
public class FindOwnedEditionsTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<IReadOnlyDictionary<string, int>> FindAsync(params string[] isbns) =>
        new FindOwnedEditionsHandler(_factory).HandleAsync(new FindOwnedEditions(isbns));

    private async Task<int> SeedBookAsync(string title, params string[] isbns)
    {
        await using var db = _factory.CreateDbContext();
        var book = TestBooks.Book(
            title,
            editions: isbns
                .Select(isbn => new Edition { Isbn = isbn, Copies = [new Copy { Condition = BookCondition.Good }] }));
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    [Fact]
    public async Task OwnedIsbns_mapToTheirBooks_andTheUnownedOneIsAbsent()
    {
        var mort = await SeedBookAsync("Mort", "9780552131063");
        var gods = await SeedBookAsync("Small Gods", "9780552134637", "0575052899");

        var owned = await FindAsync("9780552131063", "0575052899", "9780000000001");

        Assert.Equal(2, owned.Count);
        Assert.Equal(mort, owned["9780552131063"]);
        Assert.Equal(gods, owned["0575052899"]);
        Assert.False(owned.ContainsKey("9780000000001"));
    }

    [Fact]
    public async Task NoOwnedIsbn_isEmpty()
    {
        await SeedBookAsync("Mort", "9780552131063");

        Assert.Empty(await FindAsync("9780000000001", "0000000000"));
    }

    [Fact]
    public async Task EmptyInput_isEmpty()
    {
        await SeedBookAsync("Mort", "9780552131063");

        Assert.Empty(await FindAsync());
    }

    [Fact]
    public async Task SoftDeletedBooksIsbn_isAbsent()
    {
        var bookId = await SeedBookAsync("Mort", "9780552131063");
        var kept = await SeedBookAsync("Small Gods", "9780552134637");
        await using (var db = _factory.CreateDbContext())
        {
            var book = await db.Books.Include(b => b.Editions).SingleAsync(b => b.Id == bookId);
            book.SoftDelete();
            await db.SaveChangesAsync();
        }

        var owned = await FindAsync("9780552131063", "9780552134637");

        Assert.Equal(kept, Assert.Single(owned).Value);
        Assert.False(owned.ContainsKey("9780552131063"));
    }
}
