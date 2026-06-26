using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

// Loader-only since PR5 — the merge write moved to MergeBooksHandler
// (BookMergeHandlerTests). These cover the merge-preview reads.
[Trait("Category", TestCategories.Integration)]
public class BookMergeServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private BookMergeService CreateService() => new(_factory);

    [Fact]
    public async Task LoadAsync_returns_both_details()
    {
        var (winnerId, loserId) = await SeedTwoBooksAsync();

        var result = await CreateService().LoadAsync(winnerId, loserId);

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
