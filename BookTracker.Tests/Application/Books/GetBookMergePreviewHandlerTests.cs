using BookTracker.Application.Books;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the Book merge-preview read-model handler. Relocated
// from BookMergeServiceTests when the loader became GetBookMergePreview (PR6);
// the merge write is covered by BookMergeHandlerTests.
[Trait("Category", TestCategories.Integration)]
public class GetBookMergePreviewHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<BookMergeLoadResult> Preview(int idA, int idB) =>
        new GetBookMergePreviewHandler(_factory).HandleAsync(new GetBookMergePreview(idA, idB));

    [Fact]
    public async Task LoadAsync_returns_both_details()
    {
        var (winnerId, loserId) = await SeedTwoBooksAsync();

        var result = await Preview(winnerId, loserId);

        Assert.NotNull(result.Lower);
        Assert.NotNull(result.Higher);
    }

    [Fact]
    public async Task LoadAsync_headlineAuthorFollowsWorkOrder()
    {
        // A two-work book whose works have different authors; the headline uses
        // the Order-0 work. After a reorder that puts the second work first, the
        // preview headline must follow — not stay pinned to the original first.
        int bookId, otherId, firstWorkId, secondWorkId;
        using (var db = _factory.CreateDbContext())
        {
            var austen = new Author { Name = "Jane Austen" };
            var dickens = new Author { Name = "Charles Dickens" };
            var book = new Book { Title = "Two-Author Anthology" };
            var emma = new Work { Title = "Emma", WorkAuthors = [new WorkAuthor { Author = austen, Order = 0 }] };
            var oliver = new Work { Title = "Oliver Twist", WorkAuthors = [new WorkAuthor { Author = dickens, Order = 0 }] };
            book.AttachWork(emma);    // Order 0 → headline
            book.AttachWork(oliver);  // Order 1
            var other = new Book { Title = "Other", Works = [new Work { Title = "X", WorkAuthors = [new WorkAuthor { Author = austen, Order = 0 }] }] };
            db.Books.AddRange(book, other);
            await db.SaveChangesAsync();
            bookId = book.Id; otherId = other.Id; firstWorkId = emma.Id; secondWorkId = oliver.Id;
        }

        var before = await Preview(bookId, otherId);
        Assert.Equal("Jane Austen", DetailFor(before, bookId)!.AuthorName);

        // Reorder: Oliver Twist (Dickens) first.
        await new BookTracker.Application.Works.ReorderWorksHandler(_factory)
            .HandleAsync(new BookTracker.Application.Works.ReorderWorks(bookId, [secondWorkId, firstWorkId]));

        var after = await Preview(bookId, otherId);
        Assert.Equal("Charles Dickens", DetailFor(after, bookId)!.AuthorName);
    }

    private static BookMergeDetail? DetailFor(BookMergeLoadResult result, int bookId) =>
        result.Lower?.Id == bookId ? result.Lower : result.Higher;

    private async Task<(int winnerId, int loserId)> SeedTwoBooksAsync()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Shared" };
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = new Book { Title = "B", Works = [work] };
        var loser = new Book { Title = "B", Works = [work] };
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();
        return (winner.Id, loser.Id);
    }
}
