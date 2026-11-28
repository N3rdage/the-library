using BookTracker.Application.Wishlist;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the /wishlist series-gaps read model. Pins the handler's
// output (rows, ordering, values, the SeriesSlots gap rule, the soft-delete
// filter) so the switch from an Include graph to a projection can't drift it.
[Trait("Category", TestCategories.Integration)]
public class GetSeriesGapsHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<SeriesGapsResult> Load() =>
        new GetSeriesGapsHandler(_factory).HandleAsync(new GetSeriesGaps());

    private static Book NewBook(Series series, string title, int? order, string? orderDisplay = null, params Copy[] copies) => TestBooks.Book(
        title,
        series: series,
        seriesOrder: order,
        seriesOrderDisplay: orderDisplay,
        works: [TestWorks.Work(title)],
        editions: copies.Length == 0 ? null : [TestEditions.Edition(copies: copies)]);

    [Fact]
    public async Task FullyRemovedBook_doesNotOccupyItsSlot()
    {
        using (var db = _factory.CreateDbContext())
        {
            var foundation = TestSeries.Series("Foundation", SeriesType.Series, expectedCount: 3);
            var open = TestSeries.Series("Open", SeriesType.Series);
            db.Series.AddRange(foundation, open);
            db.Books.AddRange(
                NewBook(foundation, "One", 1),
                NewBook(foundation, "Two", 2, null, TestEditions.RemovedCopy()),
                NewBook(foundation, "Three", 3, null, TestEditions.RemovedCopy(), TestEditions.Copy()),
                NewBook(open, "Solo", 1, null, TestEditions.RemovedCopy()));
            await db.SaveChangesAsync();
        }

        var result = await Load();

        var gap = Assert.Single(result.Gaps);
        Assert.Equal(new[] { 2 }, gap.MissingPositions);
        Assert.Equal(2, gap.OwnedCount);
        Assert.Equal(new[] { "One", "Three" }, gap.OwnedBooks.Select(b => b.Title));
        Assert.Empty(result.OpenSeries);
    }

    [Fact]
    public async Task StructuredSeries_ReportsMissingSlots_OwnedCount_AndOwnedBooksInSeriesOrder()
    {
        using (var db = _factory.CreateDbContext())
        {
            var foundation = TestSeries.Series("Foundation", SeriesType.Series, author: "Isaac Asimov", expectedCount: 5);
            db.Series.Add(foundation);
            // Added out of order so the owned-books ordering is the handler's, not insert order.
            db.Books.AddRange(
                NewBook(foundation, "Second Foundation", 3),
                NewBook(foundation, "Foundation", 1),
                NewBook(foundation, "Prequel Novella", null));
            await db.SaveChangesAsync();
        }

        var result = await Load();

        var gap = Assert.Single(result.Gaps);
        Assert.Equal("Foundation", gap.SeriesName);
        Assert.Equal("Isaac Asimov", gap.Author);
        Assert.Equal(5, gap.ExpectedCount);
        Assert.Equal(2, gap.OwnedCount);                       // #1 and #3; the null order fills no slot
        Assert.Equal(new[] { 2, 4, 5 }, gap.MissingPositions);
        Assert.Equal(new[] { "Foundation", "Second Foundation", "Prequel Novella" },
            gap.OwnedBooks.Select(b => b.Title));              // by SeriesOrder, nulls last
        Assert.Equal(new string?[] { "1", "3", null }, gap.OwnedBooks.Select(b => b.SeriesOrderLabel));
        Assert.Empty(result.OpenSeries);
    }

    [Fact]
    public async Task FlooredInterquel_DoesNotClaimNumberedSlot_ButIsListedWithItsDisplayLabel()
    {
        using (var db = _factory.CreateDbContext())
        {
            var s = TestSeries.Series("Dune", SeriesType.Series, expectedCount: 4);
            db.Series.Add(s);
            db.Books.AddRange(
                NewBook(s, "Dune", 1),
                NewBook(s, "Interlude", 2, "2.5"),
                NewBook(s, "Out of Range", 9));               // above ExpectedCount — no slot
            await db.SaveChangesAsync();
        }

        var gap = Assert.Single((await Load()).Gaps);

        Assert.Equal(1, gap.OwnedCount);
        Assert.Equal(new[] { 2, 3, 4 }, gap.MissingPositions);
        Assert.Equal(new[] { "Dune", "Interlude", "Out of Range" }, gap.OwnedBooks.Select(b => b.Title));
        Assert.Equal(new string?[] { "1", "2.5", "9" }, gap.OwnedBooks.Select(b => b.SeriesOrderLabel));
    }

    [Fact]
    public async Task CompleteSeries_Collections_AndSoftDeletedBooks_AreHandled()
    {
        using (var db = _factory.CreateDbContext())
        {
            var complete = TestSeries.Series("Complete", SeriesType.Series, expectedCount: 2);
            var collection = TestSeries.Series("Collection", SeriesType.Collection, expectedCount: 3);
            var partial = TestSeries.Series("Partial", SeriesType.Series, expectedCount: 2);
            db.Series.AddRange(complete, collection, partial);
            var deleted = NewBook(partial, "Tombstoned #2", 2);
            deleted.SoftDelete();
            db.Books.AddRange(
                NewBook(complete, "C1", 1),
                NewBook(complete, "C2", 2),
                NewBook(collection, "Coll1", 1),
                NewBook(partial, "P1", 1),
                deleted);
            await db.SaveChangesAsync();
        }

        var gap = Assert.Single((await Load()).Gaps);   // complete + collection excluded

        Assert.Equal("Partial", gap.SeriesName);
        Assert.Equal(1, gap.OwnedCount);                 // the tombstone doesn't own #2
        Assert.Equal(new[] { 2 }, gap.MissingPositions);
        Assert.Equal(new[] { "P1" }, gap.OwnedBooks.Select(b => b.Title));
    }

    [Fact]
    public async Task StructuredSeriesWithNoBooks_ListsEverySlotMissing_AndGapsSortByName()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Series.AddRange(
                TestSeries.Series("Zelazny Amber", SeriesType.Series, expectedCount: 2),
                TestSeries.Series("Asimov Robots", SeriesType.Series, expectedCount: 3));
            await db.SaveChangesAsync();
        }

        var gaps = (await Load()).Gaps;

        Assert.Equal(new[] { "Asimov Robots", "Zelazny Amber" }, gaps.Select(g => g.SeriesName));
        Assert.Equal(new[] { 1, 2, 3 }, gaps[0].MissingPositions);
        Assert.Equal(0, gaps[0].OwnedCount);
        Assert.Empty(gaps[0].OwnedBooks);
    }

    [Fact]
    public async Task OpenSeries_ReportsOwnedCount_HighestOrder_SortedOrders_AndSortsByName()
    {
        using (var db = _factory.CreateDbContext())
        {
            var discworld = TestSeries.Series("Discworld", SeriesType.Series, author: "Terry Pratchett");
            var culture = TestSeries.Series("Culture", SeriesType.Series);
            var empty = TestSeries.Series("Empty Open", SeriesType.Series);
            var openCollection = TestSeries.Series("Open Collection", SeriesType.Collection);
            db.Series.AddRange(discworld, culture, empty, openCollection);
            db.Books.AddRange(
                NewBook(discworld, "Mort", 4),
                NewBook(discworld, "The Colour of Magic", 1),
                NewBook(discworld, "Unnumbered", null),
                NewBook(culture, "Untitled Culture", null),
                NewBook(openCollection, "Anthology", 1));
            await db.SaveChangesAsync();
        }

        var result = await Load();

        Assert.Empty(result.Gaps);
        Assert.Equal(new[] { "Culture", "Discworld" }, result.OpenSeries.Select(o => o.SeriesName));

        var culture2 = result.OpenSeries[0];
        Assert.Equal(1, culture2.OwnedCount);
        Assert.Equal(0, culture2.HighestOwnedOrder);   // no numbered books yet
        Assert.Empty(culture2.OwnedOrders);

        var dw = result.OpenSeries[1];
        Assert.Equal("Terry Pratchett", dw.Author);
        Assert.Equal(3, dw.OwnedCount);                // counts the unnumbered book too
        Assert.Equal(4, dw.HighestOwnedOrder);
        Assert.Equal(new[] { 1, 4 }, dw.OwnedOrders);
    }

    [Fact]
    public async Task OpenSeries_WithOnlySoftDeletedBooks_IsNotListed()
    {
        using (var db = _factory.CreateDbContext())
        {
            var s = TestSeries.Series("Ghost", SeriesType.Series);
            db.Series.Add(s);
            var b = NewBook(s, "Gone", 1);
            b.SoftDelete();
            db.Books.Add(b);
            await db.SaveChangesAsync();
        }

        Assert.Empty((await Load()).OpenSeries);
    }
}
