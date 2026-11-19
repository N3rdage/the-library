using BookTracker.Application.Books;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the MergeEditions command handler against the SQL
// container. Moved from EditionMergeServiceTests when the merge write became a
// command (PR5, back-end refactor); the loader's LoadAsync reads stay there.
[Trait("Category", TestCategories.Integration)]
public class EditionMergeHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<EditionMergeResult> Merge(int winnerId, int loserId) =>
        new MergeEditionsHandler(_factory).HandleAsync(new MergeEditions(winnerId, loserId));

    // ─── reassign + auto-fill ─────────────────────────────────────────

    [Fact]
    public async Task Merge_reassigns_copies_and_deletes_loser()
    {
        var (winnerId, loserId, _) = await SeedTwoEditionsOnSameBookAsync();

        // Add a copy on each side.
        using (var db = _factory.CreateDbContext())
        {
            db.Copies.Add(TestEditions.Copy(editionId: winnerId, condition: BookCondition.Good));
            db.Copies.Add(TestEditions.Copy(editionId: loserId, condition: BookCondition.Fair));
            await db.SaveChangesAsync();
        }

        var result = await Merge(winnerId, loserId);

        Assert.True(result.Success);
        Assert.Equal(1, result.CopiesReassigned);

        using var verify = _factory.CreateDbContext();
        Assert.Equal(2, verify.Copies.Count(c => c.EditionId == winnerId));
        Assert.Null(verify.Editions.FirstOrDefault(e => e.Id == loserId));
    }

    [Fact]
    public async Task Merge_auto_fills_empty_winner_fields_from_loser()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("A");
        var work = TestWorks.Work("T", author);
        var publisher = new Publisher { Name = "Acme Press" };
        var book = TestBooks.Book("B", works: [work]);
        // Winner has no date, no cover, no publisher, no ISBN.
        var winner = TestEditions.Edition(book: book, format: BookFormat.Hardcover);
        // Loser has all of them.
        var loser = TestEditions.Edition(
            book: book,
            format: BookFormat.Hardcover,
            isbn: "9780000000099",
            datePrinted: new DateOnly(1984, 6, 1),
            datePrintedPrecision: DatePrecision.Month,
            coverUrl: "https://example.com/cover.jpg",
            publisher: publisher);
        db.Books.Add(book);
        db.Editions.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

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
    public async Task Merge_preserves_populated_winner_fields()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("A");
        var work = TestWorks.Work("T", author);
        var pubA = new Publisher { Name = "Keep" };
        var pubB = new Publisher { Name = "Ignored" };
        var book = TestBooks.Book("B", works: [work]);
        // Winner already has everything.
        var winner = TestEditions.Edition(
            book: book, format: BookFormat.Hardcover,
            isbn: "9780000000111",
            datePrinted: new DateOnly(1990, 1, 1),
            coverUrl: "https://example.com/winner.jpg",
            publisher: pubA);
        var loser = TestEditions.Edition(
            book: book, format: BookFormat.Hardcover,
            isbn: "9780000000222",  // different, but winner wins
            datePrinted: new DateOnly(1985, 1, 1),
            coverUrl: "https://example.com/loser.jpg",
            publisher: pubB);
        db.Books.Add(book);
        db.Editions.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

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
    public async Task Merge_clears_ignored_duplicates_referencing_loser()
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

        await Merge(winnerId, loserId);

        using var verify = _factory.CreateDbContext();
        Assert.Empty(verify.IgnoredDuplicates);
    }

    // ─── Refusals ─────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_rejects_self_merge()
    {
        var (winnerId, _, _) = await SeedTwoEditionsOnSameBookAsync();

        var result = await Merge(winnerId, winnerId);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Merge_rejects_missing_entities()
    {
        var (winnerId, _, _) = await SeedTwoEditionsOnSameBookAsync();

        var result = await Merge(winnerId, loserId: 99999);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Merge_rejects_cross_book_merge()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("A");
        var w1 = TestWorks.Work("A1", author);
        var w2 = TestWorks.Work("A2", author);
        var b1 = TestBooks.Book("B1", works: [w1]);
        var b2 = TestBooks.Book("B2", works: [w2]);
        var e1 = TestEditions.Edition(book: b1, isbn: "9780000000001", format: BookFormat.Hardcover);
        var e2 = TestEditions.Edition(book: b2, isbn: "9780000000002", format: BookFormat.Hardcover);
        db.Books.AddRange(b1, b2);
        db.Editions.AddRange(e1, e2);
        await db.SaveChangesAsync();

        var result = await Merge(e1.Id, e2.Id);

        Assert.False(result.Success);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private async Task<(int winnerId, int loserId, int bookId)> SeedTwoEditionsOnSameBookAsync()
    {
        using var db = _factory.CreateDbContext();
        var author = Author.Create("Shared");
        var work = TestWorks.Work("T", author);
        var book = TestBooks.Book("B", works: [work]);
        var winner = TestEditions.Edition(book: book, isbn: "9780000000001", format: BookFormat.Hardcover);
        var loser = TestEditions.Edition(book: book, isbn: "9780000000002", format: BookFormat.Hardcover);
        db.Books.Add(book);
        db.Editions.AddRange(winner, loser);
        await db.SaveChangesAsync();
        return (winner.Id, loser.Id, book.Id);
    }
}
