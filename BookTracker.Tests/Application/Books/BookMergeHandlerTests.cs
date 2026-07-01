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
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = new Book { Title = "B", Works = [work] };
        var loser = new Book { Title = "B", Works = [work] };
        var loserEdition = new Edition
        {
            Book = loser,
            Isbn = "9780000000001",
            Format = BookFormat.Hardcover,
            Copies = [new Copy { Condition = BookCondition.Good }]
        };
        db.Books.AddRange(winner, loser);
        db.Editions.Add(loserEdition);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(1, result.EditionsReassigned);

        using var verify = _factory.CreateDbContext();
        // Loser invisible to normal queries (soft-deleted) ...
        Assert.Null(verify.Books.FirstOrDefault(b => b.Id == loser.Id));
        // ... but the husk row survives with DeletedAt set so the
        // catalog snapshot can tombstone it for Bookshelf clients.
        var husk = await verify.Books.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == loser.Id);
        Assert.NotNull(husk);
        Assert.NotNull(husk!.DeletedAt);
        var winnerEditions = verify.Editions.Where(e => e.BookId == winner.Id).ToList();
        Assert.Single(winnerEditions);
        Assert.Equal("9780000000001", winnerEditions[0].Isbn);
    }

    [Fact]
    public async Task Merge_unions_works_without_duplicating()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "A" };
        var sharedWork = new Work { Title = "Shared", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var loserOnlyWork = new Work { Title = "LoserOnly", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = new Book { Title = "B", Works = [sharedWork] };
        var loser = new Book { Title = "B", Works = [sharedWork, loserOnlyWork] };
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
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var tagA = new Tag { Name = "tag-a" };
        var tagB = new Tag { Name = "tag-b" };
        db.Tags.AddRange(tagA, tagB);
        var winner = new Book { Title = "B", Works = [work], Tags = [tagA] };
        var loser = new Book { Title = "B", Works = [work], Tags = [tagA, tagB] };
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
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = new Book
        {
            Title = "B",
            Works = [work],
            Rating = 0,                 // unrated → will take loser's 4
            Notes = null,               // empty → will take loser's notes
            DefaultCoverArtUrl = null,  // empty → will take loser's cover
        };
        var loser = new Book
        {
            Title = "B",
            Works = [work],
            Rating = 4,
            Notes = "Good read",
            DefaultCoverArtUrl = "https://example.com/cover.jpg",
        };
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
        var author = new Author { Name = "A" };
        var series = new Series { Name = "Dune Chronicles", Type = SeriesType.Series };
        db.Series.Add(series);
        var winner = new Book { Title = "Dune", Works = [new Work { Title = "Dune", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }] };
        var loser = new Book
        {
            Title = "Dune",
            Series = series, SeriesOrder = 1,
            Works = [new Work { Title = "Dune (dup)", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }],
        };
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
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = new Book
        {
            Title = "B", Works = [work],
            Rating = 5, Notes = "Winner notes", DefaultCoverArtUrl = "winner.jpg",
        };
        var loser = new Book
        {
            Title = "B", Works = [work],
            Rating = 1, Notes = "Loser notes", DefaultCoverArtUrl = "loser.jpg",
        };
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
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = new Book { Title = "B", Works = [work], Rating = 0 };
        var loser = new Book { Title = "B", Works = [work], Rating = 3 };
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
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = new Book { Title = "B", Works = [work], Rating = 0 };
        var loser = new Book { Title = "B", Works = [work], Rating = 0 };
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
        var author = new Author { Name = "Shared" };
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var winner = new Book { Title = "B", Works = [work] };
        var loser = new Book { Title = "B", Works = [work] };
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();
        return (winner.Id, loser.Id);
    }
}
