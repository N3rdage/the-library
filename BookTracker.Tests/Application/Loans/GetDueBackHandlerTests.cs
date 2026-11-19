using BookTracker.Application.Loans;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for GetDueBack (Home's "Due back" panel) and
// GetLoanCalendar (the rows behind the .ics downloads).
[Trait("Category", TestCategories.Integration)]
public class GetDueBackHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private static readonly DateOnly Lent = new(2026, 11, 1);

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

    private static Copy LentCopy(string borrower, DateOnly? dueOn = null)
    {
        var copy = TestEditions.Copy();
        copy.LendTo(borrower, Lent, dueOn);
        return copy;
    }

    private static Copy ReturnedCopy(string borrower, DateOnly? dueOn = null)
    {
        var copy = LentCopy(borrower, dueOn);
        copy.MarkReturned(copy.OpenLoan!.Id, Lent.AddDays(1));
        return copy;
    }

    private Task<DueBackSummary> DueBack() =>
        new GetDueBackHandler(_factory).HandleAsync(new GetDueBack());

    private Task<IReadOnlyList<LoanCalendarEntry>> Calendar(int? loanId = null, string? borrower = null) =>
        new GetLoanCalendarHandler(_factory).HandleAsync(new GetLoanCalendar(loanId, borrower));

    [Fact]
    public async Task Empty_whenNothingIsLent()
    {
        await SeedAsync("Dune", [TestEditions.Copy()]);

        var summary = await DueBack();

        Assert.Empty(summary.Dated);
        Assert.Equal(0, summary.OpenEndedCount);
    }

    [Fact]
    public async Task ProjectsTheRow()
    {
        var copy = LentCopy("Sam", Lent.AddDays(14));
        var book = await SeedAsync("Dune", [copy]);

        var row = Assert.Single((await DueBack()).Dated);

        Assert.Equal(new DueBackRow(copy.OpenLoan!.Id, book.Id, "Dune", "Sam", Lent.AddDays(14)), row);
    }

    [Fact]
    public async Task OrdersBySoonestDueDate_thenByLoanId()
    {
        await SeedAsync("Later", [LentCopy("Sam", Lent.AddDays(20))]);
        await SeedAsync("Soonest", [LentCopy("Alex", Lent.AddDays(5))]);
        await SeedAsync("Tie first", [LentCopy("Jo", Lent.AddDays(10))]);
        await SeedAsync("Tie second", [LentCopy("Sam", Lent.AddDays(10))]);

        var rows = (await DueBack()).Dated;

        Assert.Equal(new[] { "Soonest", "Tie first", "Tie second", "Later" }, rows.Select(r => r.Title));
    }

    [Fact]
    public async Task ListsAtMostTen_soonestFirst()
    {
        var copies = Enumerable.Range(0, 12).Select(i => LentCopy($"Borrower {i:00}", Lent.AddDays(30 - i))).ToList();
        await SeedAsync("Many", copies);

        var rows = (await DueBack()).Dated;

        Assert.Equal(GetDueBack.MaxDated, rows.Count);
        Assert.Equal("Borrower 11", rows[0].BorrowerName);
        Assert.Equal("Borrower 02", rows[^1].BorrowerName);
    }

    [Fact]
    public async Task CountsOpenEndedLoans_andLeavesThemOutOfTheList()
    {
        await SeedAsync("Dated", [LentCopy("Sam", Lent.AddDays(7))]);
        await SeedAsync("Open one", [LentCopy("Alex"), LentCopy("Jo")]);
        await SeedAsync("Open two", [LentCopy("Sam")]);

        var summary = await DueBack();

        Assert.Equal("Dated", Assert.Single(summary.Dated).Title);
        Assert.Equal(3, summary.OpenEndedCount);
    }

    [Fact]
    public async Task ReturnedLoans_areExcluded()
    {
        await SeedAsync("Returned dated", [ReturnedCopy("Sam", Lent.AddDays(3))]);
        await SeedAsync("Returned open-ended", [ReturnedCopy("Alex")]);
        await SeedAsync("Still out", [LentCopy("Jo", Lent.AddDays(9))]);

        var summary = await DueBack();

        Assert.Equal("Still out", Assert.Single(summary.Dated).Title);
        Assert.Equal(0, summary.OpenEndedCount);
    }

    [Fact]
    public async Task SoftDeletedBooks_areExcluded()
    {
        await SeedAsync("Gone dated", [LentCopy("Sam", Lent.AddDays(3))], deletedAt: DateTime.UtcNow);
        await SeedAsync("Gone open-ended", [LentCopy("Alex")], deletedAt: DateTime.UtcNow);
        await SeedAsync("Here", [LentCopy("Jo", Lent.AddDays(9))]);

        var summary = await DueBack();

        Assert.Equal("Here", Assert.Single(summary.Dated).Title);
        Assert.Equal(0, summary.OpenEndedCount);
    }

    [Fact]
    public async Task GetLoanCalendar_byLoan_returnsTheDatedOpenLoanOnly()
    {
        var dated = LentCopy("Sam", Lent.AddDays(14));
        var openEnded = LentCopy("Sam");
        var returned = ReturnedCopy("Sam", Lent.AddDays(2));
        await SeedAsync("Dune", [dated, openEnded, returned]);

        var entry = Assert.Single(await Calendar(loanId: dated.OpenLoan!.Id));
        Assert.Equal(new LoanCalendarEntry(dated.OpenLoan.Id, "Dune", "Sam", Lent.AddDays(14)), entry);

        Assert.Empty(await Calendar(loanId: openEnded.OpenLoan!.Id));
        Assert.Empty(await Calendar(loanId: returned.Loans.Single().Id));
        Assert.Empty(await Calendar(loanId: 999_999));
    }

    [Fact]
    public async Task GetLoanCalendar_byBorrower_returnsTheirDatedOpenLoans_soonestFirst()
    {
        await SeedAsync("Later", [LentCopy("Sam", Lent.AddDays(20))]);
        await SeedAsync("Sooner", [LentCopy("Sam", Lent.AddDays(5))]);
        await SeedAsync("Open-ended", [LentCopy("Sam")]);
        await SeedAsync("Someone else's", [LentCopy("Alex", Lent.AddDays(1))]);
        await SeedAsync("Gone", [LentCopy("Sam", Lent.AddDays(2))], deletedAt: DateTime.UtcNow);

        Assert.Equal(new[] { "Sooner", "Later" }, (await Calendar(borrower: "Sam")).Select(e => e.Title));
        Assert.Empty(await Calendar(borrower: "Nobody"));
    }
}
