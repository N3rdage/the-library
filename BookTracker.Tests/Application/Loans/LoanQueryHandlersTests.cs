using BookTracker.Application.Books;
using BookTracker.Application.Loans;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for GetOpenLoans, GetBorrowerNames and the open loan on
// GetBookDetail's CopyDetail.
[Trait("Category", TestCategories.Integration)]
public class LoanQueryHandlersTests
{
    private readonly TestDbContextFactory _factory = new();

    private static readonly DateOnly Lent = new(2026, 11, 1);

    // Seeds a book with one edition and the given copies (lend them before
    // calling); the book's lead work has `author` as its lead Author.
    private async Task<Book> SeedAsync(
        string title, string? author, IEnumerable<Copy> copies,
        string? editionCover = null, string? bookCover = null, DateTime? deletedAt = null)
    {
        await using var db = _factory.CreateDbContext();
        var works = new List<Work>();
        if (author is not null)
        {
            // An Illustrator listed first by Order must not win over the lead Author.
            works.Add(new Work
            {
                Title = title,
                WorkAuthors =
                [
                    new WorkAuthor { Author = Author.Create($"{author} Illustrator"), Order = 0, Role = AuthorRole.Illustrator },
                    new WorkAuthor { Author = Author.Create($"{author} Second"), Order = 1 },
                    new WorkAuthor { Author = Author.Create(author), Order = 0 },
                ],
            });
        }
        var book = TestBooks.Book(
            title,
            coverUrl: bookCover,
            works: works,
            editions: [TestEditions.Edition(coverUrl: editionCover, copies: copies)],
            deletedAt: deletedAt);
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    private static Copy LentCopy(string borrower, DateOnly? dueOn = null, DateOnly? lentOn = null)
    {
        var copy = TestEditions.Copy();
        copy.LendTo(borrower, lentOn ?? Lent, dueOn);
        return copy;
    }

    private static Copy ReturnedCopy(string borrower, DateOnly lentOn)
    {
        var copy = LentCopy(borrower, lentOn: lentOn);
        copy.MarkReturned(copy.OpenLoan!.Id, lentOn.AddDays(1));
        return copy;
    }

    private Task<IReadOnlyList<OpenLoanRow>> OpenLoans() =>
        new GetOpenLoansHandler(_factory).HandleAsync(new GetOpenLoans());

    private Task<IReadOnlyList<string>> BorrowerNames(string? prefix = null) =>
        new GetBorrowerNamesHandler(_factory).HandleAsync(new GetBorrowerNames(prefix));

    [Fact]
    public async Task GetOpenLoans_projectsTheRow()
    {
        var copy = LentCopy("Sam", Lent.AddDays(14));
        var book = await SeedAsync("Dune", "Frank Herbert", [copy], editionCover: "https://e/cover.jpg", bookCover: "https://b/cover.jpg");

        var row = Assert.Single(await OpenLoans());

        Assert.Equal(copy.OpenLoan!.Id, row.LoanId);
        Assert.Equal(copy.Id, row.CopyId);
        Assert.Equal(book.Id, row.BookId);
        Assert.Equal("Dune", row.Title);
        Assert.Equal("Frank Herbert", row.Author);
        Assert.Equal("https://e/cover.jpg", row.CoverUrl);
        Assert.Equal("Sam", row.BorrowerName);
        Assert.Equal(Lent, row.LentOn);
        Assert.Equal(Lent.AddDays(14), row.DueOn);
    }

    [Fact]
    public async Task GetOpenLoans_fallsBackToBookCover_andNullAuthorWithoutWorks()
    {
        await SeedAsync("Dune", author: null, [LentCopy("Sam")], editionCover: null, bookCover: "https://b/cover.jpg");

        var row = Assert.Single(await OpenLoans());

        Assert.Equal("https://b/cover.jpg", row.CoverUrl);
        Assert.Null(row.Author);
    }

    [Fact]
    public async Task GetOpenLoans_skipsReturnedLoans()
    {
        await SeedAsync("Dune", "Frank Herbert", [ReturnedCopy("Sam", Lent), LentCopy("Alex")]);

        var row = Assert.Single(await OpenLoans());
        Assert.Equal("Alex", row.BorrowerName);
    }

    [Fact]
    public async Task GetOpenLoans_skipsSoftDeletedBooks()
    {
        await SeedAsync("Gone", "Someone", [LentCopy("Sam")], deletedAt: DateTime.UtcNow);
        await SeedAsync("Here", "Someone Else", [LentCopy("Alex")]);

        var row = Assert.Single(await OpenLoans());
        Assert.Equal("Here", row.Title);
    }

    [Fact]
    public async Task GetOpenLoans_ordersByBorrower_thenDueDate_withNoDueDateLast()
    {
        await SeedAsync("A", "Author A", [LentCopy("Sam", dueOn: null)]);
        await SeedAsync("B", "Author B", [LentCopy("Sam", dueOn: Lent.AddDays(20))]);
        await SeedAsync("C", "Author C", [LentCopy("Sam", dueOn: Lent.AddDays(10))]);
        await SeedAsync("D", "Author D", [LentCopy("Alex", dueOn: null)]);

        var rows = await OpenLoans();

        Assert.Equal(new[] { "D", "C", "B", "A" }, rows.Select(r => r.Title));
    }

    [Fact]
    public async Task GetBorrowerNames_distinct_mostRecentlyLentFirst()
    {
        await SeedAsync("A", null, [ReturnedCopy("Sam", Lent)]);
        await SeedAsync("B", null, [ReturnedCopy("Alex", Lent.AddDays(3))]);
        await SeedAsync("C", null, [LentCopy("Sam", lentOn: Lent.AddDays(5))]);
        await SeedAsync("D", null, [LentCopy("Jo", lentOn: Lent.AddDays(1))]);

        Assert.Equal(new[] { "Sam", "Alex", "Jo" }, await BorrowerNames());
    }

    [Fact]
    public async Task GetBorrowerNames_prefixIsCaseInsensitive()
    {
        await SeedAsync("A", null, [LentCopy("Samantha")]);
        await SeedAsync("B", null, [LentCopy("Alex")]);
        await SeedAsync("C", null, [LentCopy("sam")]);

        var names = await BorrowerNames("SA");

        Assert.Equal(2, names.Count);
        Assert.Contains("Samantha", names);
        Assert.Contains("sam", names);
    }

    [Fact]
    public async Task GetBorrowerNames_returnsAtMostTwenty()
    {
        var copies = Enumerable.Range(0, 25).Select(i => LentCopy($"Borrower {i:00}", lentOn: Lent.AddDays(i))).ToList();
        await SeedAsync("A", null, copies);

        var names = await BorrowerNames();

        Assert.Equal(GetBorrowerNames.MaxResults, names.Count);
        Assert.Equal("Borrower 24", names[0]);
    }

    [Fact]
    public async Task GetBookDetail_carriesTheOpenLoanOnTheCopy()
    {
        var lent = LentCopy("Sam", Lent.AddDays(14));
        var returned = ReturnedCopy("Alex", Lent);
        var shelf = TestEditions.Copy();
        var book = await SeedAsync("Dune", "Frank Herbert", [lent, returned, shelf]);

        var detail = await new GetBookDetailHandler(_factory).HandleAsync(new GetBookDetail(book.Id));

        var copies = Assert.Single(detail!.Editions).Copies.ToDictionary(c => c.Id);
        Assert.Equal(new CopyLoanDetail(lent.OpenLoan!.Id, "Sam", Lent, Lent.AddDays(14)), copies[lent.Id].OpenLoan);
        Assert.Null(copies[returned.Id].OpenLoan);
        Assert.Null(copies[shelf.Id].OpenLoan);
    }
}
