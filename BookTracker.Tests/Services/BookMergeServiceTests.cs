using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests.Services;

public class BookMergeServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private BookMergeService CreateService() => new(_factory);

    // ─── LoadAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_returns_both_details()
    {
        var (winnerId, loserId) = await SeedTwoBooksAsync();

        var result = await CreateService().LoadAsync(winnerId, loserId);

        Assert.NotNull(result.Lower);
        Assert.NotNull(result.Higher);
    }

    // ─── MergeAsync — reassign + union + enrich ───────────────────────

    [Fact]
    public async Task MergeAsync_reassigns_editions_and_copies()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", Author = author };
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

        var result = await CreateService().MergeAsync(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(1, result.EditionsReassigned);

        using var verify = _factory.CreateDbContext();
        Assert.Null(verify.Books.FirstOrDefault(b => b.Id == loser.Id));
        var winnerEditions = verify.Editions.Where(e => e.BookId == winner.Id).ToList();
        Assert.Single(winnerEditions);
        Assert.Equal("9780000000001", winnerEditions[0].Isbn);
    }

    [Fact]
    public async Task MergeAsync_unions_works_without_duplicating()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "A" };
        var sharedWork = new Work { Title = "Shared", Author = author };
        var loserOnlyWork = new Work { Title = "LoserOnly", Author = author };
        var winner = new Book { Title = "B", Works = [sharedWork] };
        var loser = new Book { Title = "B", Works = [sharedWork, loserOnlyWork] };
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await CreateService().MergeAsync(winner.Id, loser.Id);

        Assert.Equal(1, result.WorksUnioned);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Books.Include(b => b.Works).First(b => b.Id == winner.Id);
        Assert.Equal(2, reloaded.Works.Count);
    }

    [Fact]
    public async Task MergeAsync_unions_tags_without_duplicating()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", Author = author };
        var tagA = new Tag { Name = "tag-a" };
        var tagB = new Tag { Name = "tag-b" };
        db.Tags.AddRange(tagA, tagB);
        var winner = new Book { Title = "B", Works = [work], Tags = [tagA] };
        var loser = new Book { Title = "B", Works = [work], Tags = [tagA, tagB] };
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await CreateService().MergeAsync(winner.Id, loser.Id);

        Assert.Equal(1, result.TagsUnioned);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Books.Include(b => b.Tags).First(b => b.Id == winner.Id);
        Assert.Equal(2, reloaded.Tags.Count);
    }

    [Fact]
    public async Task MergeAsync_auto_fills_empty_fields()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", Author = author };
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

        var result = await CreateService().MergeAsync(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(3, result.FieldsAutoFilled);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Books.First(b => b.Id == winner.Id);
        Assert.Equal(4, reloaded.Rating);
        Assert.Equal("Good read", reloaded.Notes);
        Assert.Equal("https://example.com/cover.jpg", reloaded.DefaultCoverArtUrl);
    }

    [Fact]
    public async Task MergeAsync_preserves_populated_winner_fields()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", Author = author };
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

        var result = await CreateService().MergeAsync(winner.Id, loser.Id);

        Assert.Equal(0, result.FieldsAutoFilled);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Books.First(b => b.Id == winner.Id);
        Assert.Equal(5, reloaded.Rating);
        Assert.Equal("Winner notes", reloaded.Notes);
        Assert.Equal("winner.jpg", reloaded.DefaultCoverArtUrl);
    }

    [Fact]
    public async Task MergeAsync_treats_winner_rating_zero_as_unrated()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", Author = author };
        var winner = new Book { Title = "B", Works = [work], Rating = 0 };
        var loser = new Book { Title = "B", Works = [work], Rating = 3 };
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();

        await CreateService().MergeAsync(winner.Id, loser.Id);

        using var verify = _factory.CreateDbContext();
        Assert.Equal(3, verify.Books.First(b => b.Id == winner.Id).Rating);
    }

    [Fact]
    public async Task MergeAsync_keeps_winner_rating_when_loser_also_unrated()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", Author = author };
        var winner = new Book { Title = "B", Works = [work], Rating = 0 };
        var loser = new Book { Title = "B", Works = [work], Rating = 0 };
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await CreateService().MergeAsync(winner.Id, loser.Id);

        Assert.Equal(0, result.FieldsAutoFilled);

        using var verify = _factory.CreateDbContext();
        Assert.Equal(0, verify.Books.First(b => b.Id == winner.Id).Rating);
    }

    [Fact]
    public async Task MergeAsync_clears_ignored_duplicates_referencing_loser()
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

        await CreateService().MergeAsync(winnerId, loserId);

        using var verify = _factory.CreateDbContext();
        Assert.Empty(verify.IgnoredDuplicates);
    }

    // ─── Refusals ─────────────────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_rejects_self_merge()
    {
        var (winnerId, _) = await SeedTwoBooksAsync();
        var result = await CreateService().MergeAsync(winnerId, winnerId);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task MergeAsync_rejects_missing_entities()
    {
        var (winnerId, _) = await SeedTwoBooksAsync();
        var result = await CreateService().MergeAsync(winnerId, loserId: 99999);
        Assert.False(result.Success);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private async Task<(int winnerId, int loserId)> SeedTwoBooksAsync()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Shared" };
        var work = new Work { Title = "T", Author = author };
        var winner = new Book { Title = "B", Works = [work] };
        var loser = new Book { Title = "B", Works = [work] };
        db.Books.AddRange(winner, loser);
        await db.SaveChangesAsync();
        return (winner.Id, loser.Id);
    }
}
