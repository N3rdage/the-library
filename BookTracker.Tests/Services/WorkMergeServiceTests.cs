using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

// Loader-only since PR5 — the merge write moved to MergeWorksHandler
// (WorkMergeHandlerTests). These cover the merge-preview reads, including the
// incompatibility + shared-book-count signals the preview surfaces.
[Trait("Category", TestCategories.Integration)]
public class WorkMergeServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private WorkMergeService CreateService() => new(_factory);

    [Fact]
    public async Task LoadAsync_returns_both_details_with_book_samples()
    {
        var (winnerId, loserId, _) = await SeedTwoWorksInSeparateBooksAsync(
            "The Hobbit", ["Hobbit HB"],
            "Hobbit", ["Hobbit PB"]);

        var result = await CreateService().LoadAsync(winnerId, loserId);

        Assert.NotNull(result.Lower);
        Assert.NotNull(result.Higher);
        Assert.Null(result.IncompatibilityReason);
        Assert.Equal(0, result.SharedBookCount);
    }

    [Fact]
    public async Task LoadAsync_reports_different_authors_as_incompatibility()
    {
        using var db = _factory.CreateDbContext();
        var tolkien = new Author { Name = "J.R.R. Tolkien" };
        var notTolkien = new Author { Name = "Imposter" };
        var w1 = new Work { Title = "The Hobbit", WorkAuthors = [new WorkAuthor { Author = tolkien, Order = 0 }] };
        var w2 = new Work { Title = "The Hobbit", WorkAuthors = [new WorkAuthor { Author = notTolkien, Order = 0 }] };
        db.Books.Add(new Book { Title = "A", Works = [w1] });
        db.Books.Add(new Book { Title = "B", Works = [w2] });
        await db.SaveChangesAsync();

        var result = await CreateService().LoadAsync(w1.Id, w2.Id);

        Assert.NotNull(result.IncompatibilityReason);
    }

    [Fact]
    public async Task LoadAsync_counts_books_that_contain_both_works()
    {
        // A compendium book attaches both works; the LoadAsync preview must
        // flag that so the merge confirmation surfaces the overlap.
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Agatha Christie" };
        var w1 = new Work { Title = "A", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var w2 = new Work { Title = "A", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        db.Books.Add(new Book { Title = "Solo W1", Works = [w1] });
        db.Books.Add(new Book { Title = "Solo W2", Works = [w2] });
        db.Books.Add(new Book { Title = "Compendium", Works = [w1, w2] });
        await db.SaveChangesAsync();

        var result = await CreateService().LoadAsync(w1.Id, w2.Id);

        Assert.Equal(1, result.SharedBookCount);
    }

    private async Task<(int winnerId, int loserId, int otherId)> SeedTwoWorksInSeparateBooksAsync(
        string winnerTitle, string[] winnerBooks,
        string loserTitle, string[] loserBooks)
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Shared Author" };
        var winner = new Work { Title = winnerTitle, WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var loser = new Work { Title = loserTitle, WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        foreach (var t in winnerBooks)
        {
            db.Books.Add(new Book { Title = t, Works = [winner] });
        }
        foreach (var t in loserBooks)
        {
            db.Books.Add(new Book { Title = t, Works = [loser] });
        }
        await db.SaveChangesAsync();
        return (winner.Id, loser.Id, 0);
    }
}
