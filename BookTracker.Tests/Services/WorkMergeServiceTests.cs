using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

public class WorkMergeServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private WorkMergeService CreateService() => new(_factory);

    // ─── LoadAsync ────────────────────────────────────────────────────

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
        var w1 = new Work { Title = "The Hobbit", Author = tolkien };
        var w2 = new Work { Title = "The Hobbit", Author = notTolkien };
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
        var w1 = new Work { Title = "A", Author = author };
        var w2 = new Work { Title = "A", Author = author };
        db.Books.Add(new Book { Title = "Solo W1", Works = [w1] });
        db.Books.Add(new Book { Title = "Solo W2", Works = [w2] });
        db.Books.Add(new Book { Title = "Compendium", Works = [w1, w2] });
        await db.SaveChangesAsync();

        var result = await CreateService().LoadAsync(w1.Id, w2.Id);

        Assert.Equal(1, result.SharedBookCount);
    }

    // ─── MergeAsync — reassignments ───────────────────────────────────

    [Fact]
    public async Task MergeAsync_reassigns_solo_book_to_winner()
    {
        var (winnerId, loserId, _) = await SeedTwoWorksInSeparateBooksAsync(
            "The Hobbit", ["Hobbit HB"],
            "Hobbit", ["Hobbit PB"]);

        var result = await CreateService().MergeAsync(winnerId, loserId);

        Assert.True(result.Success);
        Assert.Equal(1, result.BooksReassigned);
        Assert.Equal(0, result.BooksAlreadyShared);

        using var db = _factory.CreateDbContext();
        Assert.Null(db.Works.FirstOrDefault(w => w.Id == loserId));
        var pb = db.Books.Single(b => b.Title == "Hobbit PB");
        var pbWorks = db.Books.Where(b => b.Id == pb.Id).SelectMany(b => b.Works).ToList();
        Assert.Single(pbWorks);
        Assert.Equal(winnerId, pbWorks[0].Id);
    }

    [Fact]
    public async Task MergeAsync_drops_loser_from_books_that_already_contain_winner()
    {
        // Book C contains both works. Post-merge it should contain only the
        // winner — the loser-side BookWork row gets dropped. No PK violation.
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Agatha Christie" };
        var winner = new Work { Title = "Style", Author = author };
        var loser = new Work { Title = "Style", Author = author };
        db.Books.Add(new Book { Title = "Solo W2", Works = [loser] });
        db.Books.Add(new Book { Title = "Compendium", Works = [winner, loser] });
        await db.SaveChangesAsync();

        var result = await CreateService().MergeAsync(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(1, result.BooksReassigned);
        Assert.Equal(1, result.BooksAlreadyShared);

        using var verify = _factory.CreateDbContext();
        var compendium = verify.Books.Single(b => b.Title == "Compendium");
        var compWorks = verify.Books.Where(b => b.Id == compendium.Id).SelectMany(b => b.Works).ToList();
        Assert.Single(compWorks);
        Assert.Equal(winner.Id, compWorks[0].Id);
    }

    [Fact]
    public async Task MergeAsync_clears_ignored_duplicates_referencing_loser()
    {
        var (winnerId, loserId, otherId) = await SeedThreeWorksAsync();

        using (var db = _factory.CreateDbContext())
        {
            db.IgnoredDuplicates.Add(new IgnoredDuplicate
            {
                EntityType = DuplicateEntityType.Work,
                LowerId = Math.Min(winnerId, loserId),
                HigherId = Math.Max(winnerId, loserId)
            });
            db.IgnoredDuplicates.Add(new IgnoredDuplicate
            {
                EntityType = DuplicateEntityType.Work,
                LowerId = Math.Min(winnerId, otherId),
                HigherId = Math.Max(winnerId, otherId)
            });
            await db.SaveChangesAsync();
        }

        await CreateService().MergeAsync(winnerId, loserId);

        using var verify = _factory.CreateDbContext();
        Assert.Single(verify.IgnoredDuplicates);
    }

    // ─── MergeAsync — refusals ────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_rejects_self_merge()
    {
        var (winnerId, _, _) = await SeedTwoWorksInSeparateBooksAsync(
            "The Hobbit", ["Hobbit HB"],
            "Hobbit", ["Hobbit PB"]);

        var result = await CreateService().MergeAsync(winnerId, winnerId);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task MergeAsync_rejects_missing_entities()
    {
        var (winnerId, _, _) = await SeedTwoWorksInSeparateBooksAsync(
            "The Hobbit", ["Hobbit HB"],
            "Hobbit", ["Hobbit PB"]);

        var result = await CreateService().MergeAsync(winnerId, loserId: 9999);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task MergeAsync_rejects_different_authors()
    {
        using var db = _factory.CreateDbContext();
        var tolkien = new Author { Name = "J.R.R. Tolkien" };
        var notTolkien = new Author { Name = "Imposter" };
        var w1 = new Work { Title = "The Hobbit", Author = tolkien };
        var w2 = new Work { Title = "The Hobbit", Author = notTolkien };
        db.Books.Add(new Book { Title = "A", Works = [w1] });
        db.Books.Add(new Book { Title = "B", Works = [w2] });
        await db.SaveChangesAsync();

        var result = await CreateService().MergeAsync(w1.Id, w2.Id);

        Assert.False(result.Success);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private async Task<(int winnerId, int loserId, int otherId)> SeedTwoWorksInSeparateBooksAsync(
        string winnerTitle, string[] winnerBooks,
        string loserTitle, string[] loserBooks)
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Shared Author" };
        var winner = new Work { Title = winnerTitle, Author = author };
        var loser = new Work { Title = loserTitle, Author = author };
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

    private async Task<(int winnerId, int loserId, int otherId)> SeedThreeWorksAsync()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Shared Author" };
        var winner = new Work { Title = "W", Author = author };
        var loser = new Work { Title = "L", Author = author };
        var other = new Work { Title = "O", Author = author };
        db.Books.Add(new Book { Title = "BW", Works = [winner] });
        db.Books.Add(new Book { Title = "BL", Works = [loser] });
        db.Books.Add(new Book { Title = "BO", Works = [other] });
        await db.SaveChangesAsync();
        return (winner.Id, loser.Id, other.Id);
    }
}
