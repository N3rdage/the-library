using BookTracker.Application.Works;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the MergeWorks command handler against the SQL
// container. Moved from WorkMergeServiceTests when the merge write became a
// command (PR5, back-end refactor); the loader's LoadAsync reads stay there.
[Trait("Category", TestCategories.Integration)]
public class WorkMergeHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<WorkMergeResult> Merge(int winnerId, int loserId) =>
        new MergeWorksHandler(_factory).HandleAsync(new MergeWorks(winnerId, loserId));

    // ─── reassignments ────────────────────────────────────────────────

    [Fact]
    public async Task Merge_reassigns_solo_book_to_winner()
    {
        var (winnerId, loserId, _) = await SeedTwoWorksInSeparateBooksAsync(
            "The Hobbit", ["Hobbit HB"],
            "Hobbit", ["Hobbit PB"]);

        var result = await Merge(winnerId, loserId);

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
    public async Task Merge_drops_loser_from_books_that_already_contain_winner()
    {
        // Book C contains both works. Post-merge it should contain only the
        // winner — the loser-side BookWork row gets dropped. No PK violation.
        using var db = _factory.CreateDbContext();
        var author = Author.Create("Agatha Christie");
        var winner = TestWorks.Work("Style", author);
        var loser = TestWorks.Work("Style", author);
        db.Books.Add(TestBooks.Book("Solo W2", works: [loser]));
        db.Books.Add(TestBooks.Book("Compendium", works: [winner, loser]));
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

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
    public async Task Merge_clears_ignored_duplicates_referencing_loser()
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

        await Merge(winnerId, loserId);

        using var verify = _factory.CreateDbContext();
        Assert.Single(verify.IgnoredDuplicates);
    }

    // ─── refusals ─────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_rejects_self_merge()
    {
        var (winnerId, _, _) = await SeedTwoWorksInSeparateBooksAsync(
            "The Hobbit", ["Hobbit HB"],
            "Hobbit", ["Hobbit PB"]);

        var result = await Merge(winnerId, winnerId);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Merge_rejects_missing_entities()
    {
        var (winnerId, _, _) = await SeedTwoWorksInSeparateBooksAsync(
            "The Hobbit", ["Hobbit HB"],
            "Hobbit", ["Hobbit PB"]);

        var result = await Merge(winnerId, loserId: 9999);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Merge_rejects_different_authors()
    {
        using var db = _factory.CreateDbContext();
        var tolkien = Author.Create("J.R.R. Tolkien");
        var notTolkien = Author.Create("Imposter");
        var w1 = TestWorks.Work("The Hobbit", tolkien);
        var w2 = TestWorks.Work("The Hobbit", notTolkien);
        db.Books.Add(TestBooks.Book("A", works: [w1]));
        db.Books.Add(TestBooks.Book("B", works: [w2]));
        await db.SaveChangesAsync();

        var result = await Merge(w1.Id, w2.Id);

        Assert.False(result.Success);
    }

    // ─── Auto-fill empties ────────────────────────────────────────────

    [Fact]
    public async Task Merge_auto_fills_empty_winner_fields_from_loser()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("Shared");
        var horror = new Genre { Name = "Horror" };
        var fantasy = new Genre { Name = "Fantasy" };
        db.Genres.AddRange(horror, fantasy);
        await db.SaveChangesAsync();

        // Winner: bare title and author only.
        var winner = TestWorks.Work("Gunslinger", author);
        // Loser: subtitle, first-pub date, genres.
        var loser = TestWorks.Work(
            "Gunslinger",
            author,
            subtitle: "Dark Tower I",
            firstPublishedDate: new DateOnly(1982, 6, 10),
            firstPublishedDatePrecision: DatePrecision.Day,
            genres: [horror, fantasy]);
        db.Books.Add(TestBooks.Book("Winner Book", works: [winner]));
        db.Books.Add(TestBooks.Book("Loser Book", works: [loser]));
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);
        // 3 "fields": subtitle, firstPub date, genres-union (counted once).
        // Series is NOT carried across a Work merge — it lives on the Book now.
        Assert.Equal(3, result.FieldsAutoFilled);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Works
            .Include(w => w.Genres)
            .First(w => w.Id == winner.Id);
        Assert.Equal("Dark Tower I", reloaded.Subtitle);
        Assert.Equal(new DateOnly(1982, 6, 10), reloaded.FirstPublishedDate);
        Assert.Equal(2, reloaded.Genres.Count);
    }

    [Fact]
    public async Task Merge_preserves_populated_winner_fields_during_auto_fill()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("Shared");

        var winner = TestWorks.Work(
            "T",
            author,
            subtitle: "Keep Me",
            firstPublishedDate: new DateOnly(2000, 1, 1),
            firstPublishedDatePrecision: DatePrecision.Day);
        var loser = TestWorks.Work(
            "T",
            author,
            subtitle: "Ignored",
            firstPublishedDate: new DateOnly(1900, 1, 1));
        db.Books.Add(TestBooks.Book("BW", works: [winner]));
        db.Books.Add(TestBooks.Book("BL", works: [loser]));
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(0, result.FieldsAutoFilled);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Works.First(w => w.Id == winner.Id);
        Assert.Equal("Keep Me", reloaded.Subtitle);
        Assert.Equal(new DateOnly(2000, 1, 1), reloaded.FirstPublishedDate);
    }

    [Fact]
    public async Task Merge_unions_genres_without_duplicates()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("Shared");
        var horror = new Genre { Name = "Horror" };
        var fantasy = new Genre { Name = "Fantasy" };
        var mystery = new Genre { Name = "Mystery" };
        db.Genres.AddRange(horror, fantasy, mystery);
        await db.SaveChangesAsync();

        var winner = TestWorks.Work("T", author, genres: [horror, fantasy]);
        var loser = TestWorks.Work("T", author, genres: [fantasy, mystery]);
        db.Books.Add(TestBooks.Book("BW", works: [winner]));
        db.Books.Add(TestBooks.Book("BL", works: [loser]));
        await db.SaveChangesAsync();

        await Merge(winner.Id, loser.Id);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Works.Include(w => w.Genres).First(w => w.Id == winner.Id);
        Assert.Equal(3, reloaded.Genres.Count);
    }

    // ─── Role-aware (Phase G) ─────────────────────────────────────────

    [Fact]
    public async Task Merge_allows_merge_when_author_set_matches_distinctly_but_roles_differ()
    {
        // Winner credits Tolkien as Author AND Illustrator (two WorkAuthor rows,
        // same AuthorId). Loser credits Tolkien just as Author (one row). The
        // distinct author set is {Tolkien} on both sides — the merge should
        // proceed, not be rejected as "different authors".
        using var db = _factory.CreateDbContext();
        var tolkien = Author.Create("Tolkien");
        db.Authors.Add(tolkien);
        await db.SaveChangesAsync();

        var winner = TestWorks.Work(
            "The Hobbit",
            authorships:
            [
                TestWorks.Authorship(tolkien),
                TestWorks.Authorship(tolkien, 0, AuthorRole.Illustrator),
            ]);
        var loser = TestWorks.Work("Hobbit", tolkien);
        db.Books.Add(TestBooks.Book("Winner Book", works: [winner]));
        db.Books.Add(TestBooks.Book("Loser Book", works: [loser]));
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private async Task<(int winnerId, int loserId, int otherId)> SeedTwoWorksInSeparateBooksAsync(
        string winnerTitle, string[] winnerBooks,
        string loserTitle, string[] loserBooks)
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("Shared Author");
        var winner = TestWorks.Work(winnerTitle, author);
        var loser = TestWorks.Work(loserTitle, author);
        foreach (var t in winnerBooks)
        {
            db.Books.Add(TestBooks.Book(t, works: [winner]));
        }
        foreach (var t in loserBooks)
        {
            db.Books.Add(TestBooks.Book(t, works: [loser]));
        }
        await db.SaveChangesAsync();
        return (winner.Id, loser.Id, 0);
    }

    private async Task<(int winnerId, int loserId, int otherId)> SeedThreeWorksAsync()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("Shared Author");
        var winner = TestWorks.Work("W", author);
        var loser = TestWorks.Work("L", author);
        var other = TestWorks.Work("O", author);
        db.Books.Add(TestBooks.Book("BW", works: [winner]));
        db.Books.Add(TestBooks.Book("BL", works: [loser]));
        db.Books.Add(TestBooks.Book("BO", works: [other]));
        await db.SaveChangesAsync();
        return (winner.Id, loser.Id, other.Id);
    }
}
