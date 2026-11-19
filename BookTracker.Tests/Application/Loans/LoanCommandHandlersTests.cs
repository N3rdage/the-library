using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Loans;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for LendCopies / ReturnLoan, and for the loan rules the
// Book-level commands (DeleteBook, MergeBooks) and the database enforce.
[Trait("Category", TestCategories.Integration)]
public class LoanCommandHandlersTests
{
    private readonly TestDbContextFactory _factory = new();

    private static readonly DateOnly Lent = new(2026, 11, 1);

    // One book, one edition, `copies` copies; returns the book id and copy ids.
    private async Task<(int BookId, int[] CopyIds)> SeedBookAsync(string title = "Dune", int copies = 2, string? isbn = null)
    {
        await using var db = _factory.CreateDbContext();
        var seeded = Enumerable.Range(0, copies).Select(_ => TestEditions.Copy()).ToList();
        var book = TestBooks.Book(title, editions: [TestEditions.Edition(isbn: isbn, copies: seeded)]);
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return (book.Id, seeded.Select(c => c.Id).ToArray());
    }

    private Task Lend(IReadOnlyList<int> copyIds, string name = "Sam", DateOnly? lentOn = null, DateOnly? dueOn = null) =>
        new LendCopiesHandler(_factory).HandleAsync(new LendCopies(copyIds, name, lentOn ?? Lent, dueOn));

    private async Task<List<Loan>> LoansAsync()
    {
        await using var db = _factory.CreateDbContext();
        return await db.Loans.OrderBy(l => l.Id).ToListAsync();
    }

    [Fact]
    public async Task LendCopies_lendsEveryCopyToOneBorrower()
    {
        var (_, copyIds) = await SeedBookAsync(copies: 2);

        await Lend(copyIds, "  Sam  ", Lent, Lent.AddDays(14));

        var loans = await LoansAsync();
        Assert.Equal(2, loans.Count);
        Assert.Equal(copyIds.Order(), loans.Select(l => l.CopyId).Order());
        Assert.All(loans, l =>
        {
            Assert.Equal("Sam", l.BorrowerName);
            Assert.Equal(Lent, l.LentOn);
            Assert.Equal(Lent.AddDays(14), l.DueOn);
            Assert.Null(l.ReturnedOn);
        });
    }

    [Fact]
    public async Task LendCopies_duplicateIds_lendOnce()
    {
        var (_, copyIds) = await SeedBookAsync(copies: 1);

        await Lend([copyIds[0], copyIds[0]]);

        Assert.Single(await LoansAsync());
    }

    [Fact]
    public async Task LendCopies_missingCopy_throwsNotFound_andSavesNothing()
    {
        var (_, copyIds) = await SeedBookAsync(copies: 1);

        await Assert.ThrowsAsync<NotFoundException>(() => Lend([copyIds[0], 424242]));

        Assert.Empty(await LoansAsync());
    }

    [Fact]
    public async Task LendCopies_oneCopyAlreadyLent_throwsDomainRule_andSavesNothing()
    {
        var (_, copyIds) = await SeedBookAsync(copies: 2);
        await Lend([copyIds[0]], "Sam");

        await Assert.ThrowsAsync<DomainRuleException>(() => Lend(copyIds, "Alex"));

        var loans = await LoansAsync();
        var only = Assert.Single(loans);
        Assert.Equal(copyIds[0], only.CopyId);
        Assert.Equal("Sam", only.BorrowerName);
    }

    [Fact]
    public async Task LendCopies_blankName_throwsDomainRule_andSavesNothing()
    {
        var (_, copyIds) = await SeedBookAsync(copies: 1);

        await Assert.ThrowsAsync<DomainRuleException>(() => Lend(copyIds, "  "));

        Assert.Empty(await LoansAsync());
    }

    [Fact]
    public async Task ReturnLoan_setsReturnedOn_andKeepsHistory()
    {
        var (_, copyIds) = await SeedBookAsync(copies: 1);
        await Lend(copyIds);
        var loanId = (await LoansAsync()).Single().Id;

        await new ReturnLoanHandler(_factory).HandleAsync(new ReturnLoan(loanId, Lent.AddDays(5)));

        var loan = Assert.Single(await LoansAsync());
        Assert.Equal(Lent.AddDays(5), loan.ReturnedOn);
    }

    [Fact]
    public async Task ReturnLoan_thenLendAgain_keepsBothLoans()
    {
        var (_, copyIds) = await SeedBookAsync(copies: 1);
        await Lend(copyIds, "Sam");
        var first = (await LoansAsync()).Single().Id;
        await new ReturnLoanHandler(_factory).HandleAsync(new ReturnLoan(first, Lent.AddDays(5)));

        await Lend(copyIds, "Alex", Lent.AddDays(6));

        var loans = await LoansAsync();
        Assert.Equal(2, loans.Count);
        Assert.NotNull(loans[0].ReturnedOn);
        Assert.Equal("Alex", loans[1].BorrowerName);
        Assert.Null(loans[1].ReturnedOn);
    }

    [Fact]
    public async Task ReturnLoan_missingLoan_throwsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ReturnLoanHandler(_factory).HandleAsync(new ReturnLoan(424242, Lent)));
    }

    [Fact]
    public async Task ReturnLoan_alreadyReturned_throwsDomainRule_andKeepsFirstReturnDate()
    {
        var (_, copyIds) = await SeedBookAsync(copies: 1);
        await Lend(copyIds);
        var loanId = (await LoansAsync()).Single().Id;
        await new ReturnLoanHandler(_factory).HandleAsync(new ReturnLoan(loanId, Lent.AddDays(5)));

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new ReturnLoanHandler(_factory).HandleAsync(new ReturnLoan(loanId, Lent.AddDays(9))));

        Assert.Equal(Lent.AddDays(5), (await LoansAsync()).Single().ReturnedOn);
    }

    [Fact]
    public async Task ReturnLoan_oldLoanWhileCopyIsLentAgain_throwsDomainRule_andLeavesNewLoanOpen()
    {
        var (_, copyIds) = await SeedBookAsync(copies: 1);
        await Lend(copyIds, "Sam");
        var first = (await LoansAsync()).Single().Id;
        await new ReturnLoanHandler(_factory).HandleAsync(new ReturnLoan(first, Lent.AddDays(1)));
        await Lend(copyIds, "Alex", Lent.AddDays(2));

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new ReturnLoanHandler(_factory).HandleAsync(new ReturnLoan(first, Lent.AddDays(3))));

        Assert.Null((await LoansAsync())[1].ReturnedOn);
    }

    [Fact]
    public async Task ReturnLoan_beforeLentOn_throwsDomainRule_andLeavesLoanOpen()
    {
        var (_, copyIds) = await SeedBookAsync(copies: 1);
        await Lend(copyIds);
        var loanId = (await LoansAsync()).Single().Id;

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new ReturnLoanHandler(_factory).HandleAsync(new ReturnLoan(loanId, Lent.AddDays(-1))));

        Assert.Null((await LoansAsync()).Single().ReturnedOn);
    }

    [Fact]
    public async Task Database_rejectsSecondOpenLoanOnSameCopy()
    {
        var (_, copyIds) = await SeedBookAsync(copies: 1);

        // Two contexts each see the copy with no loan, so each entity check
        // passes; the filtered unique index stops the second save.
        await using var first = _factory.CreateDbContext();
        await using var second = _factory.CreateDbContext();
        var a = await first.Copies.Include(c => c.Loans).SingleAsync(c => c.Id == copyIds[0]);
        var b = await second.Copies.Include(c => c.Loans).SingleAsync(c => c.Id == copyIds[0]);
        a.LendTo("Sam", Lent, null);
        b.LendTo("Alex", Lent, null);
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task DeleteBook_withCopyLentOut_throwsDomainRule_andKeepsBookAndLoan()
    {
        var (bookId, copyIds) = await SeedBookAsync(copies: 1);
        await Lend(copyIds);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new DeleteBookHandler(_factory).HandleAsync(new DeleteBook(bookId)));

        await using var db = _factory.CreateDbContext();
        Assert.NotNull(await db.Books.FindAsync(bookId));
        Assert.Single(await db.Loans.ToListAsync());
    }

    [Fact]
    public async Task DeleteBook_withOnlyReturnedLoans_deletes()
    {
        var (bookId, copyIds) = await SeedBookAsync(copies: 1);
        await Lend(copyIds);
        var loanId = (await LoansAsync()).Single().Id;
        await new ReturnLoanHandler(_factory).HandleAsync(new ReturnLoan(loanId, Lent.AddDays(1)));

        await new DeleteBookHandler(_factory).HandleAsync(new DeleteBook(bookId));

        await using var db = _factory.CreateDbContext();
        Assert.Null(await db.Books.FirstOrDefaultAsync(b => b.Id == bookId));
        // The copy is hard-removed, and its loan history cascades with it.
        Assert.Empty(await db.Loans.ToListAsync());
    }

    [Fact]
    public async Task MergeBooks_movesLentCopiesWithTheirLoans()
    {
        var (winnerId, _) = await SeedBookAsync("Dune", copies: 1, isbn: "9780000000001");
        var (loserId, loserCopies) = await SeedBookAsync("Dune", copies: 1, isbn: "9780000000002");
        await Lend(loserCopies, "Sam");

        var result = await new MergeBooksHandler(_factory).HandleAsync(new MergeBooks(winnerId, loserId));

        Assert.True(result.Success);
        await using var db = _factory.CreateDbContext();
        var loan = await db.Loans
            .Include(l => l.Copy).ThenInclude(c => c.Edition)
            .SingleAsync();
        Assert.Equal(loserCopies[0], loan.CopyId);
        Assert.Equal(winnerId, loan.Copy.Edition.BookId);
        Assert.Null(loan.ReturnedOn);
    }
}
