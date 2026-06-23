using BookTracker.Application.Wishlist;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the Wishlist command handlers against the SQL container.
[Trait("Category", TestCategories.Integration)]
public class WishlistCommandHandlersTests
{
    private readonly TestDbContextFactory _factory = new();

    private AddWishlistSeriesSlotsHandler SlotsHandler() =>
        new(_factory, NullLogger<AddWishlistSeriesSlotsHandler>.Instance);

    private async Task<int> SeedSeriesAsync(string name = "Discworld", string? author = "Terry Pratchett")
    {
        await using var db = _factory.CreateDbContext();
        var s = new Series { Name = name, Type = SeriesType.Series, Author = author };
        db.Series.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    private async Task<int> SeedWishlistItemAsync(string title, string author = "A", string? isbn = null,
        int? seriesId = null, int? seriesOrder = null)
    {
        await using var db = _factory.CreateDbContext();
        var item = new WishlistItem
        {
            Title = title, Author = author, Isbn = isbn,
            SeriesId = seriesId, SeriesOrder = seriesOrder,
            Isbns = isbn is null ? [] : [new WishlistItemIsbn { Isbn = isbn }],
        };
        db.WishlistItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    // --- AddWishlistItem -----------------------------------------------------

    [Fact]
    public async Task AddWishlistItem_persistsDualWrittenIsbn_andReturnsResult()
    {
        var result = await new AddWishlistItemHandler(_factory).HandleAsync(new AddWishlistItem(
            "Mort", "Terry Pratchett", WishlistPriority.High, ["111", "111", "222"], "https://c/m.jpg"));

        Assert.NotNull(result);
        await using var db = _factory.CreateDbContext();
        var item = await db.WishlistItems.Include(w => w.Isbns).SingleAsync(w => w.Id == result!.Id);
        Assert.Equal("Mort", item.Title);
        Assert.Equal(WishlistPriority.High, item.Priority);
        Assert.Equal("111", item.Isbn);                                        // legacy column = primary
        Assert.Equal(new[] { "111", "222" }, item.Isbns.Select(i => i.Isbn));  // table = deduped
        Assert.Equal("https://c/m.jpg", item.CoverUrl);
        Assert.Equal("111", result!.PrimaryIsbn);
    }

    [Fact]
    public async Task AddWishlistItem_blankTitle_returnsNull_persistsNothing()
    {
        var result = await new AddWishlistItemHandler(_factory).HandleAsync(new AddWishlistItem(
            "   ", "A", WishlistPriority.Medium, [], null));

        Assert.Null(result);
        await using var db = _factory.CreateDbContext();
        Assert.Empty(db.WishlistItems);
    }

    [Fact]
    public async Task AddWishlistItem_blankAuthor_fallsBackToUnknown()
    {
        var result = await new AddWishlistItemHandler(_factory).HandleAsync(new AddWishlistItem(
            "Untitled Find", "  ", WishlistPriority.Low, [], null));

        Assert.Equal("Unknown", result!.Author);
    }

    // --- AddWishlistSeriesSlots ----------------------------------------------

    [Fact]
    public async Task AddWishlistSeriesSlots_createsStubsWithSeriesLink()
    {
        var seriesId = await SeedSeriesAsync("Foundation", "Isaac Asimov");

        var added = await SlotsHandler().HandleAsync(new AddWishlistSeriesSlots(seriesId, [1, 2, 3]));

        Assert.Equal(3, added);
        await using var db = _factory.CreateDbContext();
        var stubs = await db.WishlistItems.Where(w => w.SeriesId == seriesId).OrderBy(w => w.SeriesOrder).ToListAsync();
        Assert.Equal(3, stubs.Count);
        Assert.Equal("Foundation #1", stubs[0].Title);
        Assert.Equal("Isaac Asimov", stubs[0].Author);
        Assert.Equal(1, stubs[0].SeriesOrder);
    }

    [Fact]
    public async Task AddWishlistSeriesSlots_skipsAlreadyWishlistedSlots()
    {
        var seriesId = await SeedSeriesAsync();
        await SeedWishlistItemAsync("Discworld #2", seriesId: seriesId, seriesOrder: 2);

        var added = await SlotsHandler().HandleAsync(new AddWishlistSeriesSlots(seriesId, [1, 2, 3]));

        Assert.Equal(2, added); // slot 2 already present → only 1 + 3 added
        await using var db = _factory.CreateDbContext();
        Assert.Equal(3, await db.WishlistItems.CountAsync(w => w.SeriesId == seriesId));
    }

    [Fact]
    public async Task AddWishlistSeriesSlots_filtersNonPositiveAndDedups()
    {
        var seriesId = await SeedSeriesAsync();

        var added = await SlotsHandler().HandleAsync(new AddWishlistSeriesSlots(seriesId, [0, -1, 4, 4]));

        Assert.Equal(1, added); // only slot 4, once
    }

    [Fact]
    public async Task AddWishlistSeriesSlots_unknownSeries_returnsZero()
    {
        var added = await SlotsHandler().HandleAsync(new AddWishlistSeriesSlots(999999, [1, 2]));
        Assert.Equal(0, added);
    }

    // --- RemoveWishlistItem --------------------------------------------------

    [Fact]
    public async Task RemoveWishlistItem_removesItem_andCascadesIsbns()
    {
        var itemId = await SeedWishlistItemAsync("Mort", isbn: "9780552131063");

        await new RemoveWishlistItemHandler(_factory).HandleAsync(new RemoveWishlistItem(itemId));

        await using var db = _factory.CreateDbContext();
        Assert.Null(await db.WishlistItems.FindAsync(itemId));
        Assert.Empty(db.Set<WishlistItemIsbn>()); // children cascade-deleted
    }

    [Fact]
    public async Task RemoveWishlistItem_missing_isNoOp()
    {
        await new RemoveWishlistItemHandler(_factory).HandleAsync(new RemoveWishlistItem(999999));
    }

    // --- MarkWishlistItemBought ----------------------------------------------

    [Fact]
    public async Task MarkWishlistItemBought_createsTaggedBookWithWork_andRemovesItem()
    {
        var itemId = await SeedWishlistItemAsync("Guards! Guards!", "Terry Pratchett");

        var bookId = await new MarkWishlistItemBoughtHandler(_factory).HandleAsync(new MarkWishlistItemBought(itemId));

        Assert.NotNull(bookId);
        await using var db = _factory.CreateDbContext();
        var book = await db.Books
            .Include(b => b.Tags)
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .SingleAsync(b => b.Id == bookId);
        Assert.Equal("Guards! Guards!", book.Title);
        Assert.Contains(book.Tags, t => t.Name == "follow-up");
        Assert.Equal("Terry Pratchett", book.Works.Single().WorkAuthors.Single().Author.Name);
        Assert.Null(await db.WishlistItems.FindAsync(itemId)); // promoted away
    }

    [Fact]
    public async Task MarkWishlistItemBought_withIsbn_createsEditionAndCopy()
    {
        var itemId = await SeedWishlistItemAsync("Mort", "Terry Pratchett", isbn: "9780552131063");

        var bookId = await new MarkWishlistItemBoughtHandler(_factory).HandleAsync(new MarkWishlistItemBought(itemId));

        await using var db = _factory.CreateDbContext();
        var edition = await db.Editions.Include(e => e.Copies).SingleAsync(e => e.BookId == bookId);
        Assert.Equal("9780552131063", edition.Isbn);
        Assert.Equal(BookFormat.TradePaperback, edition.Format);
        Assert.Equal(BookCondition.Good, edition.Copies.Single().Condition);
    }

    [Fact]
    public async Task MarkWishlistItemBought_reusesExistingFollowUpTag()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Tags.Add(new Tag { Name = "follow-up" });
            await db.SaveChangesAsync();
        }
        var itemId = await SeedWishlistItemAsync("Sourcery", "Terry Pratchett");

        await new MarkWishlistItemBoughtHandler(_factory).HandleAsync(new MarkWishlistItemBought(itemId));

        await using var db2 = _factory.CreateDbContext();
        Assert.Equal(1, await db2.Tags.CountAsync(t => t.Name == "follow-up")); // not duplicated
    }

    [Fact]
    public async Task MarkWishlistItemBought_missingItem_returnsNull_createsNoBook()
    {
        var bookId = await new MarkWishlistItemBoughtHandler(_factory).HandleAsync(new MarkWishlistItemBought(999999));

        Assert.Null(bookId);
        await using var db = _factory.CreateDbContext();
        Assert.Empty(db.Books);
    }

    [Fact]
    public async Task MarkWishlistItemBought_routesEditionThroughAggregate_trimsIsbnAndSeedsOneCopy()
    {
        // Padded ISBN — book.AddEdition should TrimToNull it and seed the first Copy.
        var itemId = await SeedWishlistItemAsync("Reaper Man", "Terry Pratchett", isbn: "  9780552134644  ");

        var bookId = await new MarkWishlistItemBoughtHandler(_factory).HandleAsync(new MarkWishlistItemBought(itemId));

        await using var db = _factory.CreateDbContext();
        var edition = await db.Editions.Include(e => e.Copies).SingleAsync(e => e.BookId == bookId);
        Assert.Equal("9780552134644", edition.Isbn);   // normalised by the aggregate factory
        var copy = Assert.Single(edition.Copies);       // factory guarantees the first copy
        Assert.Equal(BookCondition.Good, copy.Condition);
    }
}
