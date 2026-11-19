using BookTracker.Application.Books;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the MergeBooks command handler against the SQL
// container. Moved from BookMergeServiceTests when the merge write became a
// command (PR5, back-end refactor); the loader's LoadAsync reads stay there.
[Trait("Category", TestCategories.Integration)]
public class BookMergeHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<BookMergeResult> Merge(int winnerId, int loserId) =>
        new MergeBooksHandler(_factory).HandleAsync(new MergeBooks(winnerId, loserId));

    // ─── reassign + union + enrich ────────────────────────────────────

    [Fact]
    public async Task Merge_reassigns_editions_and_copies()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("A");
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = TestBooks.Book("B", works: [work]);
        var loser = TestBooks.Book("B", works: [work]);
        var hardcover = new Edition
        {
            Book = loser,
            Isbn = "9780000000001",
            Format = BookFormat.Hardcover,
            Copies = [new Copy { Condition = BookCondition.Good }, new Copy { Condition = BookCondition.Fair }]
        };
        var paperback = new Edition
        {
            Book = loser,
            Isbn = "9780000000002",
            Format = BookFormat.TradePaperback,
            Copies = [new Copy { Condition = BookCondition.Good }]
        };
        db.Books.AddRange(winner, loser);
        db.Editions.AddRange(hardcover, paperback);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(2, result.EditionsReassigned);

        using var verify = _factory.CreateDbContext();
        // Loser invisible to normal queries (soft-deleted) ...
        Assert.Null(verify.Books.FirstOrDefault(b => b.Id == loser.Id));
        // ... but the husk row survives with DeletedAt set so the
        // catalog snapshot can tombstone it for Bookshelf clients.
        var husk = await verify.Books.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == loser.Id);
        Assert.NotNull(husk);
        Assert.NotNull(husk!.DeletedAt);
        // The same Edition rows (not replacements) now belong to the winner,
        // each still holding its Copies.
        var moved = verify.Editions.Include(e => e.Copies)
            .Where(e => e.Id == hardcover.Id || e.Id == paperback.Id)
            .ToDictionary(e => e.Id);
        Assert.Equal(2, moved.Count);
        Assert.Equal(winner.Id, moved[hardcover.Id].BookId);
        Assert.Equal(winner.Id, moved[paperback.Id].BookId);
        Assert.Equal(2, moved[hardcover.Id].Copies.Count);
        Assert.Single(moved[paperback.Id].Copies);
        Assert.Equal(2, verify.Editions.Count(e => e.BookId == winner.Id));
    }

    [Fact]
    public async Task Merge_unions_works_without_duplicating()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("A");
        var sharedWork = new Work { Title = "Shared", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var loserOnlyWork = new Work { Title = "LoserOnly", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = TestBooks.Book("B", works: [sharedWork]);
        var loser = TestBooks.Book("B", works: [sharedWork, loserOnlyWork]);
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.Equal(1, result.WorksUnioned);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Books.Include(b => b.Works).First(b => b.Id == winner.Id);
        Assert.Equal(2, reloaded.Works.Count);
    }

    [Fact]
    public async Task Merge_unions_tags_without_duplicating()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("A");
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var tagA = new Tag { Name = "tag-a" };
        var tagB = new Tag { Name = "tag-b" };
        db.Tags.AddRange(tagA, tagB);
        var winner = TestBooks.Book("B", works: [work], tags: [tagA]);
        var loser = TestBooks.Book("B", works: [work], tags: [tagA, tagB]);
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.Equal(1, result.TagsUnioned);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Books.Include(b => b.Tags).First(b => b.Id == winner.Id);
        Assert.Equal(2, reloaded.Tags.Count);
    }

    [Fact]
    public async Task Merge_auto_fills_empty_fields()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("A");
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = TestBooks.Book(
            "B",
            works: [work],
            rating: 0,                  // unrated → will take loser's 4
            notes: null,                // empty → will take loser's notes
            coverUrl: null);            // empty → will take loser's cover
        var loser = TestBooks.Book(
            "B",
            works: [work],
            rating: 4,
            notes: "Good read",
            coverUrl: "https://example.com/cover.jpg");
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(3, result.FieldsAutoFilled);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Books.First(b => b.Id == winner.Id);
        Assert.Equal(4, reloaded.Rating);
        Assert.Equal("Good read", reloaded.Notes);
        Assert.Equal("https://example.com/cover.jpg", reloaded.DefaultCoverArtUrl);
    }

    [Fact]
    public async Task Merge_carries_loser_series_to_unseriesed_winner()
    {
        // Regression for the Work→Book series move (TODO #56): series is a Book
        // field now, so a book merge must carry it explicitly — pre-move it rode
        // along on the unioned Works. A series-less winner takes the loser's series.
        using var db = _factory.CreateDbContext();
        var author = Author.Create("A");
        var series = new Series { Name = "Dune Chronicles", Type = SeriesType.Series };
        db.Series.Add(series);
        var winner = TestBooks.Book("Dune", works: [new Work { Title = "Dune", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }]);
        var loser = TestBooks.Book(
            "Dune",
            series: series, seriesOrder: 1,
            works: [new Work { Title = "Dune (dup)", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }]);
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);
        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Books.First(b => b.Id == winner.Id);
        Assert.Equal(series.Id, reloaded.SeriesId);   // winner now in the series (was series-less)
        Assert.Equal(1, reloaded.SeriesOrder);
    }

    [Fact]
    public async Task Merge_preserves_populated_winner_fields()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("A");
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = TestBooks.Book(
            "B", works: [work],
            rating: 5, notes: "Winner notes", coverUrl: "winner.jpg");
        var loser = TestBooks.Book(
            "B", works: [work],
            rating: 1, notes: "Loser notes", coverUrl: "loser.jpg");
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.Equal(0, result.FieldsAutoFilled);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Books.First(b => b.Id == winner.Id);
        Assert.Equal(5, reloaded.Rating);
        Assert.Equal("Winner notes", reloaded.Notes);
        Assert.Equal("winner.jpg", reloaded.DefaultCoverArtUrl);
    }

    [Fact]
    public async Task Merge_treats_winner_rating_zero_as_unrated()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("A");
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = TestBooks.Book("B", works: [work], rating: 0);
        var loser = TestBooks.Book("B", works: [work], rating: 3);
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();

        await Merge(winner.Id, loser.Id);

        using var verify = _factory.CreateDbContext();
        Assert.Equal(3, verify.Books.First(b => b.Id == winner.Id).Rating);
    }

    [Fact]
    public async Task Merge_keeps_winner_rating_when_loser_also_unrated()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("A");
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = TestBooks.Book("B", works: [work], rating: 0);
        var loser = TestBooks.Book("B", works: [work], rating: 0);
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.Equal(0, result.FieldsAutoFilled);

        using var verify = _factory.CreateDbContext();
        Assert.Equal(0, verify.Books.First(b => b.Id == winner.Id).Rating);
    }

    [Fact]
    public async Task Merge_clears_ignored_duplicates_referencing_loser()
    {
        var (winnerId, loserId) = await SeedTwoBooksAsync();
        using (var db = _factory.CreateDbContext())
        {
            db.IgnoredDuplicates.Add(new IgnoredDuplicate
            {
                EntityType = DuplicateEntityType.Book,
                LowerId = Math.Min(winnerId, loserId),
                HigherId = Math.Max(winnerId, loserId)
            });
            await db.SaveChangesAsync();
        }

        await Merge(winnerId, loserId);

        using var verify = _factory.CreateDbContext();
        Assert.Empty(verify.IgnoredDuplicates);
    }

    // ─── Refusals ─────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_rejects_self_merge()
    {
        var (winnerId, _) = await SeedTwoBooksAsync();
        var result = await Merge(winnerId, winnerId);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Merge_rejects_missing_entities()
    {
        var (winnerId, _) = await SeedTwoBooksAsync();
        var result = await Merge(winnerId, loserId: 99999);
        Assert.False(result.Success);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private async Task<(int winnerId, int loserId)> SeedTwoBooksAsync()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("Shared");
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = TestBooks.Book("B", works: [work]);
        var loser = TestBooks.Book("B", works: [work]);
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();
        return (winner.Id, loser.Id);
    }
}
