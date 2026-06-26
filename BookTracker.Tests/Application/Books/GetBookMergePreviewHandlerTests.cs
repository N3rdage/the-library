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
