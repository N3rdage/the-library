using BookTracker.Mobile.Cache.Presentation;
using BookTracker.Shared.Online;

namespace BookTracker.Mobile.Cache.Tests;

[Trait("Category", "Unit")]
public class WorkEditionsOrderingTests
{
    private static OnlineEdition Edition(string? isbn, string? date = null, int? ownedBookId = null, string? publisher = "Penguin") =>
        new(isbn, "Dune", publisher, date, "Paperback", null, ownedBookId);

    [Fact]
    public void OrdersOwnedFirst_ThenNewestFirst_ThenUndatedLast()
    {
        var editions = new[]
        {
            Edition("A", "1990"),
            Edition("B"),
            Edition("C", "2005"),
            Edition("D", "1970", ownedBookId: 7),
            Edition("E", "March 3, 2010"),
        };

        var rows = WorkEditionsOrdering.Arrange(editions, []);

        Assert.Equal(["D", "E", "C", "A", "B"], rows.Select(r => r.Isbn));
    }

    [Fact]
    public void OwnedByCachedIsbn_IsMarkedOwnedAndSortsFirst()
    {
        var editions = new[] { Edition("9780441172719", "2005"), Edition("9780441013593", "1990") };

        var rows = WorkEditionsOrdering.Arrange(editions, ["978-0-441-01359-3"]);

        Assert.Equal(["9780441013593", "9780441172719"], rows.Select(r => r.Isbn));
        Assert.True(rows[0].IsOwned);
        Assert.False(rows[1].IsOwned);
    }

    [Fact]
    public void RepeatedIsbn_IsOneRow_OwnedWhenAnyOccurrenceIs()
    {
        var editions = new[] { Edition("A", "2000"), Edition("a", "1999", ownedBookId: 3), Edition("B", "2001") };

        var rows = WorkEditionsOrdering.Arrange(editions, []);

        Assert.Equal(["A", "B"], rows.Select(r => r.Isbn));
        Assert.True(rows[0].IsOwned);
        Assert.Equal("2000", rows[0].PublishDate);
    }

    [Fact]
    public void EditionsWithoutIsbn_AreEachKept()
    {
        var editions = new[] { Edition(null, "1990", publisher: "X"), Edition(" ", "1991", publisher: "Y") };

        var rows = WorkEditionsOrdering.Arrange(editions, []);

        Assert.Equal(["Y", "X"], rows.Select(r => r.Publisher));
        Assert.All(rows, r => Assert.Null(r.Isbn));
    }

    [Fact]
    public void EqualDates_KeepTheEndpointOrder()
    {
        var editions = new[] { Edition("A", "1990"), Edition("B", "1990"), Edition("C", "1990") };

        Assert.Equal(["A", "B", "C"], WorkEditionsOrdering.Arrange(editions, []).Select(r => r.Isbn));
    }

    [Fact]
    public void Empty_GivesNoRows() =>
        Assert.Empty(WorkEditionsOrdering.Arrange([], ["A"]));

    [Theory]
    [InlineData("1995", 1995, 1, 1)]
    [InlineData("March 1995", 1995, 3, 1)]
    [InlineData("1995-06-02", 1995, 6, 2)]
    [InlineData("c. 1887", 1887, 1, 1)]
    [InlineData("[1901?]", 1901, 1, 1)]
    public void ParseDate_ReadsFullDatesAndBareYears(string text, int year, int month, int day) =>
        Assert.Equal(new DateTime(year, month, day), WorkEditionsOrdering.ParseDate(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("12345")]
    public void ParseDate_UnreadableIsNull(string? text) =>
        Assert.Null(WorkEditionsOrdering.ParseDate(text));

    [Fact]
    public void Meta_JoinsThePartsPresent()
    {
        var full = new WorkEditionRow("A", "Dune", "Penguin", "1995", "Paperback", null, false);
        var sparse = new WorkEditionRow(null, "Dune", " ", null, "Hardcover", null, false);

        Assert.Equal("Penguin  ·  Paperback  ·  1995", WorkEditionsOrdering.Meta(full));
        Assert.Equal("Hardcover", WorkEditionsOrdering.Meta(sparse));
    }
}
