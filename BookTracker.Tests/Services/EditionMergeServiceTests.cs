using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests.Services;

[Trait("Category", TestCategories.Integration)]
public class EditionMergeServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private EditionMergeService CreateService() => new(_factory);

    // ─── LoadAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_returns_details_for_same_book_editions()
    {
        var (winnerId, loserId, _) = await SeedTwoEditionsOnSameBookAsync();

        var result = await CreateService().LoadAsync(winnerId, loserId);

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

        var result = await CreateService().LoadAsync(e1.Id, e2.Id);

        Assert.NotNull(result.IncompatibilityReason);
    }

    // ─── MergeAsync — reassign + auto-fill ────────────────────────────

    [Fact]
    public async Task MergeAsync_reassigns_copies_and_deletes_loser()
    {
        var (winnerId, loserId, bookId) = await SeedTwoEditionsOnSameBookAsync();

        // Add a copy on each side.
        using (var db = _factory.CreateDbContext())
        {
            db.Copies.Add(new Copy { EditionId = winnerId, Condition = BookCondition.Good });
            db.Copies.Add(new Copy { EditionId = loserId, Condition = BookCondition.Fair });
            await db.SaveChangesAsync();
        }

        var result = await CreateService().MergeAsync(winnerId, loserId);

        Assert.True(result.Success);
        Assert.Equal(1, result.CopiesReassigned);

        using var verify = _factory.CreateDbContext();
        Assert.Equal(2, verify.Copies.Count(c => c.EditionId == winnerId));
        Assert.Null(verify.Editions.FirstOrDefault(e => e.Id == loserId));
    }

    [Fact]
    public async Task MergeAsync_auto_fills_empty_winner_fields_from_loser()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var publisher = new Publisher { Name = "Acme Press" };
        var book = new Book { Title = "B", Works = [work] };
        // Winner has no date, no cover, no publisher, no ISBN.
        var winner = new Edition { Book = book, Format = BookFormat.Hardcover };
        // Loser has all of them.
        var loser = new Edition
        {
            Book = book,
            Format = BookFormat.Hardcover,
            Isbn = "9780000000099",
            DatePrinted = new DateOnly(1984, 6, 1),
            DatePrintedPrecision = DatePrecision.Month,
            CoverUrl = "https://example.com/cover.jpg",
            Publisher = publisher
        };
        db.Books.Add(book);
        db.Editions.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await CreateService().MergeAsync(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(4, result.FieldsAutoFilled);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Editions.Include(e => e.Publisher).First(e => e.Id == winner.Id);
        Assert.Equal("9780000000099", reloaded.Isbn);
        Assert.Equal(new DateOnly(1984, 6, 1), reloaded.DatePrinted);
        Assert.Equal(DatePrecision.Month, reloaded.DatePrintedPrecision);
        Assert.Equal("https://example.com/cover.jpg", reloaded.CoverUrl);
        Assert.Equal("Acme Press", reloaded.Publisher?.Name);
    }

    [Fact]
    public async Task MergeAsync_preserves_populated_winner_fields()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "A" };
        var work = new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var pubA = new Publisher { Name = "Keep" };
        var pubB = new Publisher { Name = "Ignored" };
        var book = new Book { Title = "B", Works = [work] };
        // Winner already has everything.
        var winner = new Edition
        {
            Book = book, Format = BookFormat.Hardcover,
            Isbn = "9780000000111",
            DatePrinted = new DateOnly(1990, 1, 1),
            CoverUrl = "https://example.com/winner.jpg",
            Publisher = pubA
        };
        var loser = new Edition
        {
            Book = book, Format = BookFormat.Hardcover,
            Isbn = "9780000000222",  // different, but winner wins
            DatePrinted = new DateOnly(1985, 1, 1),
            CoverUrl = "https://example.com/loser.jpg",
            Publisher = pubB
        };
        db.Books.Add(book);
        db.Editions.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await CreateService().MergeAsync(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(0, result.FieldsAutoFilled);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Editions.Include(e => e.Publisher).First(e => e.Id == winner.Id);
        Assert.Equal("9780000000111", reloaded.Isbn);
        Assert.Equal(new DateOnly(1990, 1, 1), reloaded.DatePrinted);
        Assert.Equal("https://example.com/winner.jpg", reloaded.CoverUrl);
        Assert.Equal("Keep", reloaded.Publisher?.Name);
    }

    [Fact]
    public async Task MergeAsync_clears_ignored_duplicates_referencing_loser()
    {
        var (winnerId, loserId, _) = await SeedTwoEditionsOnSameBookAsync();
        using (var db = _factory.CreateDbContext())
        {
            db.IgnoredDuplicates.Add(new IgnoredDuplicate
            {
                EntityType = DuplicateEntityType.Edition,
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
        var (winnerId, _, _) = await SeedTwoEditionsOnSameBookAsync();

        var result = await CreateService().MergeAsync(winnerId, winnerId);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task MergeAsync_rejects_missing_entities()
    {
        var (winnerId, _, _) = await SeedTwoEditionsOnSameBookAsync();

        var result = await CreateService().MergeAsync(winnerId, loserId: 99999);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task MergeAsync_rejects_cross_book_merge()
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

        var result = await CreateService().MergeAsync(e1.Id, e2.Id);

        Assert.False(result.Success);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

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
