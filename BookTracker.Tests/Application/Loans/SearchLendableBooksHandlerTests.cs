using BookTracker.Application.Loans;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for SearchLendableBooks: title match, free copies only,
// soft-deleted books hidden, order and cap.
[Trait("Category", TestCategories.Integration)]
public class SearchLendableBooksHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private static readonly DateOnly Lent = new(2026, 11, 1);

    // Seeds a book with the given editions; the lead work has `author` as its
    // lead Author when one is given.
    private async Task<Book> SeedAsync(
        string title, IEnumerable<Edition> editions, string? author = null, DateTime? deletedAt = null)
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
                    new WorkAuthor { Author = Author.Create(author), Order = 0 },
                ],
            });
        }
        var book = TestBooks.Book(title, works: works, editions: editions, deletedAt: deletedAt);
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    private static Copy LentCopy(string borrower = "Sam")
    {
        var copy = TestEditions.Copy();
        copy.LendTo(borrower, Lent, null);
        return copy;
    }

    private static Copy ReturnedCopy()
    {
        var copy = LentCopy();
        copy.MarkReturned(copy.OpenLoan!.Id, Lent.AddDays(1));
        return copy;
    }

    private Task<IReadOnlyList<LendableBook>> Search(string query) =>
        new SearchLendableBooksHandler(_factory).HandleAsync(new SearchLendableBooks(query));

    [Fact]
    public async Task ProjectsTheBook_WithItsFreeCopiesOnly()
    {
        var free = TestEditions.Copy(BookCondition.VeryGood);
        var returned = ReturnedCopy();
        var lent = LentCopy();
        var book = await SeedAsync("Dune",
            [TestEditions.Edition(isbn: "9780441013593", format: BookFormat.Hardcover, copies: [free, lent, returned])],
            author: "Frank Herbert");

        var row = Assert.Single(await Search("dune"));

        Assert.Equal(book.Id, row.BookId);
        Assert.Equal("Dune", row.Title);
        Assert.Equal("Frank Herbert", row.Author);
        Assert.Equal(new[] { free.Id, returned.Id }.Order(), row.Copies.Select(c => c.CopyId).Order());
        var freeRow = row.Copies.Single(c => c.CopyId == free.Id);
        Assert.Equal(BookCondition.VeryGood, freeRow.Condition);
        Assert.Equal("Hardcover · 9780441013593", freeRow.EditionLabel);
        Assert.DoesNotContain(row.Copies, c => c.CopyId == lent.Id);
    }

    [Fact]
    public async Task ExcludesBooksWithNoFreeCopy()
    {
        await SeedAsync("Dune", [TestEditions.Edition(copies: [LentCopy()])]);
        await SeedAsync("Dune Messiah", [TestEditions.Edition(copies: [TestEditions.Copy()])]);

        var row = Assert.Single(await Search("dune"));
        Assert.Equal("Dune Messiah", row.Title);
    }

    [Fact]
    public async Task MatchesAnywhereInTheTitle_CaseInsensitive_AndTrimmed()
    {
        await SeedAsync("Children of Dune", [TestEditions.Edition(copies: [TestEditions.Copy()])]);
        await SeedAsync("Foundation", [TestEditions.Edition(copies: [TestEditions.Copy()])]);

        var row = Assert.Single(await Search("  DUNE "));
        Assert.Equal("Children of Dune", row.Title);
    }

    [Fact]
    public async Task BlankQuery_ReturnsNothing()
    {
        await SeedAsync("Dune", [TestEditions.Edition(copies: [TestEditions.Copy()])]);

        Assert.Empty(await Search("   "));
    }

    [Fact]
    public async Task SkipsSoftDeletedBooks()
    {
        await SeedAsync("Dune", [TestEditions.Edition(copies: [TestEditions.Copy()])], deletedAt: DateTime.UtcNow);

        Assert.Empty(await Search("dune"));
    }

    [Fact]
    public async Task ListsFreeCopiesAcrossEditions_AndNullAuthorWithoutWorks()
    {
        var hardcover = TestEditions.Copy();
        var paperback = TestEditions.Copy(BookCondition.Fair);
        await SeedAsync("Dune",
        [
            TestEditions.Edition(format: BookFormat.Hardcover, datePrinted: new DateOnly(1965, 8, 1), copies: [hardcover]),
            TestEditions.Edition(format: BookFormat.MassMarketPaperback, datePrinted: new DateOnly(1990, 1, 1), copies: [paperback]),
        ]);

        var row = Assert.Single(await Search("dune"));

        Assert.Null(row.Author);
        Assert.Equal(new[] { hardcover.Id, paperback.Id }, row.Copies.Select(c => c.CopyId));
        Assert.Equal("Mass Market Paperback · no ISBN", row.Copies[1].EditionLabel);
    }

    [Fact]
    public async Task OrdersByTitle_AndReturnsAtMostTwenty()
    {
        for (var i = 24; i >= 0; i--)
            await SeedAsync($"Book {i:00}", [TestEditions.Edition(copies: [TestEditions.Copy()])]);

        var rows = await Search("book");

        Assert.Equal(SearchLendableBooks.MaxResults, rows.Count);
        Assert.Equal("Book 00", rows[0].Title);
        Assert.Equal("Book 19", rows[^1].Title);
    }
}
