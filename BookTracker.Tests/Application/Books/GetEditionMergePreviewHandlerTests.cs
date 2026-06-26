using BookTracker.Application.Books;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the Edition merge-preview read-model handler. Relocated
// from EditionMergeServiceTests when the loader became GetEditionMergePreview
// (PR6); the merge write is covered by EditionMergeHandlerTests.
[Trait("Category", TestCategories.Integration)]
public class GetEditionMergePreviewHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<EditionMergeLoadResult> Preview(int idA, int idB) =>
        new GetEditionMergePreviewHandler(_factory).HandleAsync(new GetEditionMergePreview(idA, idB));

    [Fact]
    public async Task LoadAsync_returns_details_for_same_book_editions()
    {
        var (winnerId, loserId, _) = await SeedTwoEditionsOnSameBookAsync();

        var result = await Preview(winnerId, loserId);

        Assert.NotNull(result.Lower);
        Assert.NotNull(result.Higher);
        Assert.Null(result.IncompatibilityReason);
    }

    [Fact]
    public async Task LoadAsync_flags_cross_book_editions_as_incompatible()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "A" };
        var w1 = new Work { Title = "A1", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var w2 = new Work { Title = "A2", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var b1 = new Book { Title = "B1", Works = [w1] };
        var b2 = new Book { Title = "B2", Works = [w2] };
        var e1 = new Edition { Book = b1, Isbn = "9780000000001", Format = BookFormat.Hardcover };
        var e2 = new Edition { Book = b2, Isbn = "9780000000002", Format = BookFormat.Hardcover };
        db.Books.AddRange(b1, b2);
        db.Editions.AddRange(e1, e2);
        await db.SaveChangesAsync();

        var result = await Preview(e1.Id, e2.Id);

        Assert.NotNull(result.IncompatibilityReason);
    }

    private async Task<(int winnerId, int loserId, int bookId)> SeedTwoEditionsOnSameBookAsync()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Shared" };
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var book = new Book { Title = "B", Works = [work] };
        var winner = new Edition { Book = book, Isbn = "9780000000001", Format = BookFormat.Hardcover };
        var loser = new Edition { Book = book, Isbn = "9780000000002", Format = BookFormat.Hardcover };
        db.Books.Add(book);
        db.Editions.AddRange(winner, loser);
        await db.SaveChangesAsync();
        return (winner.Id, loser.Id, book.Id);
    }
}
