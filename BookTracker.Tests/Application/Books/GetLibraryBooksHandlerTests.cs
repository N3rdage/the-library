using BookTracker.Application.Books;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the Library flat list's loan marker: BookListItem.LentTo
// is the borrower of an open loan on any copy, else null.
[Trait("Category", TestCategories.Integration)]
public class GetLibraryBooksHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private static readonly DateOnly Lent = new(2026, 11, 1);

    private static readonly LibraryFilter NoFilter = new(null, null, 0, 0, 0, null, null);

    private async Task SeedAsync(string title, params Copy[] copies)
    {
        await using var db = _factory.CreateDbContext();
        db.Books.Add(TestBooks.Book(title, editions: [TestEditions.Edition(copies: copies)]));
        await db.SaveChangesAsync();
    }

    private async Task<Dictionary<string, BookListItem>> LibraryByTitle()
    {
        var result = await new GetLibraryBooksHandler(_factory).HandleAsync(new GetLibraryBooks(NoFilter, 1));
        return result.Books.ToDictionary(b => b.Title);
    }

    [Fact]
    public async Task LentTo_isTheOpenLoansBorrower_elseNull()
    {
        var lent = TestEditions.Copy();
        lent.LendTo("Sam", Lent, dueOn: null);
        await SeedAsync("Dune", lent, TestEditions.Copy());

        var returned = TestEditions.Copy();
        returned.LendTo("Alex", Lent, dueOn: null);
        returned.MarkReturned(returned.OpenLoan!.Id, Lent.AddDays(3));
        await SeedAsync("Emma", returned);

        await SeedAsync("Free", TestEditions.Copy());

        var books = await LibraryByTitle();

        Assert.Equal(3, books.Count);
        Assert.Equal("Sam", books["Dune"].LentTo);
        Assert.Null(books["Emma"].LentTo);
        Assert.Null(books["Free"].LentTo);
    }
}
