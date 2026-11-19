using BookTracker.Application.Loans;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for GetLoanHistory: returned loans only, newest return
// first, optionally narrowed to one book, soft-deleted books hidden.
[Trait("Category", TestCategories.Integration)]
public class GetLoanHistoryHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private static readonly DateOnly Lent = new(2026, 10, 1);

    private async Task<Book> SeedAsync(string title, IEnumerable<Copy> copies, DateTime? deletedAt = null)
    {
        await using var db = _factory.CreateDbContext();
        var book = TestBooks.Book(
            title,
            editions: [TestEditions.Edition(copies: copies)],
            deletedAt: deletedAt);
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    private static Copy LentCopy(string borrower, DateOnly? lentOn = null)
    {
        var copy = TestEditions.Copy();
        copy.LendTo(borrower, lentOn ?? Lent, dueOn: null);
        return copy;
    }

    private static Copy ReturnedCopy(string borrower, DateOnly returnedOn, DateOnly? lentOn = null)
    {
        var copy = LentCopy(borrower, lentOn);
        copy.MarkReturned(copy.OpenLoan!.Id, returnedOn);
        return copy;
    }

    private Task<IReadOnlyList<LoanHistoryRow>> History(int? bookId = null) =>
        new GetLoanHistoryHandler(_factory).HandleAsync(new GetLoanHistory(bookId));

    [Fact]
    public async Task ProjectsTheReturnedLoan()
    {
        var copy = ReturnedCopy("Sam", Lent.AddDays(9));
        var book = await SeedAsync("Dune", [copy]);

        var row = Assert.Single(await History());

        Assert.Equal(copy.Loans[0].Id, row.LoanId);
        Assert.Equal(book.Id, row.BookId);
        Assert.Equal("Dune", row.Title);
        Assert.Equal("Sam", row.BorrowerName);
        Assert.Equal(Lent, row.LentOn);
        Assert.Equal(Lent.AddDays(9), row.ReturnedOn);
    }

    [Fact]
    public async Task SkipsOpenLoans()
    {
        await SeedAsync("Dune", [ReturnedCopy("Sam", Lent.AddDays(3)), LentCopy("Alex")]);

        var row = Assert.Single(await History());
        Assert.Equal("Sam", row.BorrowerName);
    }

    [Fact]
    public async Task SkipsALoanReturnedThenLentAgain_onlyTheReturnedOneShows()
    {
        var book = await SeedAsync("Dune", [ReturnedCopy("Sam", Lent.AddDays(3))]);

        await using (var db = _factory.CreateDbContext())
        {
            var copy = db.Copies.Single(c => c.Edition.BookId == book.Id);
            await db.Entry(copy).Collection(c => c.Loans).LoadAsync();
            copy.LendTo("Alex", Lent.AddDays(5), dueOn: null);
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(await History());
        Assert.Equal("Sam", row.BorrowerName);
    }

    [Fact]
    public async Task OrdersByReturnDate_newestFirst()
    {
        await SeedAsync("A", [ReturnedCopy("Sam", Lent.AddDays(2))]);
        await SeedAsync("B", [ReturnedCopy("Alex", Lent.AddDays(10))]);
        await SeedAsync("C", [ReturnedCopy("Jo", Lent.AddDays(5))]);

        var rows = await History();

        Assert.Equal(new[] { "B", "C", "A" }, rows.Select(r => r.Title));
    }

    [Fact]
    public async Task FiltersByBook()
    {
        var dune = await SeedAsync("Dune", [ReturnedCopy("Sam", Lent.AddDays(2)), ReturnedCopy("Alex", Lent.AddDays(4))]);
        await SeedAsync("Emma", [ReturnedCopy("Jo", Lent.AddDays(3))]);

        var rows = await History(dune.Id);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(dune.Id, r.BookId));
        Assert.Equal(new[] { "Alex", "Sam" }, rows.Select(r => r.BorrowerName));
    }

    [Fact]
    public async Task SkipsSoftDeletedBooks()
    {
        await SeedAsync("Gone", [ReturnedCopy("Sam", Lent.AddDays(2))], deletedAt: DateTime.UtcNow);
        await SeedAsync("Here", [ReturnedCopy("Alex", Lent.AddDays(2))]);

        var row = Assert.Single(await History());
        Assert.Equal("Here", row.Title);
    }
}
