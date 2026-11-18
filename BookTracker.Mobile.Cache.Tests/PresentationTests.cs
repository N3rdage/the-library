using BookTracker.Mobile.Cache.Presentation;
using BookTracker.Shared.Catalog;
using BookTracker.Shared.Wishlist;

namespace BookTracker.Mobile.Cache.Tests;

// Pins the Bookshelf page helpers that live in Mobile.Cache/Presentation so
// they can be covered without the MAUI runtime. These describe today's
// behaviour, quirks included.
[Trait("Category", "Unit")]
public class IsbnInputTests
{
    [Theory]
    [InlineData("0306406152", true)]      // ISBN-10
    [InlineData("9780306406157", true)]   // ISBN-13
    [InlineData("080442957X", true)]      // ISBN-10 with check X
    [InlineData("080442957x", true)]      // lower-case x
    [InlineData("12345678901", true)]     // 11 digits: accepted today (not a real ISBN length)
    [InlineData("123456789012", true)]    // 12 digits: accepted today (not a real ISBN length)
    [InlineData("123456789", false)]      // too short
    [InlineData("12345678901234", false)] // too long
    [InlineData("08044X2957", false)]     // X not in the last position
    [InlineData("978-030640615", false)]  // 13 chars with hyphens: not stripped before the check
    [InlineData("978030640615 ", false)]  // trailing space
    [InlineData("", false)]
    [InlineData("foundation", false)]
    public void LooksLikeIsbn(string query, bool expected)
    {
        Assert.Equal(expected, IsbnInput.LooksLikeIsbn(query));
    }

    [Theory]
    [InlineData("978-0-306-40615-7", "9780306406157")]
    [InlineData(" 080442957X ", "080442957X")]
    [InlineData("080442957x", "080442957x")]   // case is kept
    [InlineData("X12X", "X12X")]              // an X anywhere survives, not only trailing
    [InlineData("ISBN 978 0306406157", "9780306406157")]
    [InlineData("", "")]
    public void CleanIsbn_KeepsDigitsAndX(string raw, string expected)
    {
        Assert.Equal(expected, IsbnInput.CleanIsbn(raw));
    }
}

[Trait("Category", "Unit")]
public class BookMetaFormatterTests
{
    private static BookSnapshot Book(int rating, string status) =>
        new(1, "Foundation", "Asimov", ["Asimov"], status, rating, [], null, null);

    [Theory]
    [InlineData(0, "", "")]
    [InlineData(0, "   ", "")]
    [InlineData(0, "Read", "Read")]
    [InlineData(3, "", "★★★☆☆")]
    [InlineData(3, "Read", "★★★☆☆  ·  Read")]
    [InlineData(5, "Reading", "★★★★★  ·  Reading")]
    [InlineData(1, "Unread", "★☆☆☆☆  ·  Unread")]
    [InlineData(9, "Read", "★★★★★  ·  Read")]   // clamped to five stars
    [InlineData(-2, "Read", "Read")]            // a negative rating shows no stars
    public void FormatBookMeta(int rating, string status, string expected)
    {
        Assert.Equal(expected, BookMetaFormatter.FormatBookMeta(Book(rating, status)));
    }
}

[Trait("Category", "Unit")]
public class EditionFormatNameTests
{
    [Theory]
    [InlineData("Hardcover", "Hardcover")]
    [InlineData("TradePaperback", "Trade Paperback")]
    [InlineData("MassMarketPaperback", "Mass Market Paperback")]
    [InlineData("EBook", "EBook")]          // consecutive capitals get no space
    [InlineData("PDFCopy", "PDFCopy")]      // nor does an acronym run into a word
    [InlineData("audio", "audio")]
    [InlineData("", "(unknown)")]
    public void FormatPrettyName(string format, string expected)
    {
        Assert.Equal(expected, EditionFormatName.FormatPrettyName(format));
    }
}

[Trait("Category", "Unit")]
public class SeriesGapFormatterTests
{
    [Fact]
    public void MissingParts_EmptyInput_NoPills()
    {
        Assert.Empty(SeriesGapFormatter.MissingParts([]));
    }

    [Fact]
    public void MissingParts_SingleSlot()
    {
        Assert.Equal(["#4"], SeriesGapFormatter.MissingParts([4]));
    }

    [Fact]
    public void MissingParts_RunOfTwo_StaysAsSeparatePills()
    {
        Assert.Equal(["#2", "#3"], SeriesGapFormatter.MissingParts([2, 3]));
    }

    [Fact]
    public void MissingParts_RunOfThree_CollapsesToRange()
    {
        Assert.Equal(["#2–#4"], SeriesGapFormatter.MissingParts([2, 3, 4]));
    }

    [Fact]
    public void MissingParts_MixedRunsAndSingles()
    {
        Assert.Equal(
            ["#1", "#3–#6", "#8", "#9", "#11–#13"],
            SeriesGapFormatter.MissingParts([1, 3, 4, 5, 6, 8, 9, 11, 12, 13]));
    }

    [Fact]
    public void MissingParts_UnsortedInput_OnlyAscendingNeighboursJoin()
    {
        // No sort happens here; the cache hands the orders over ascending.
        Assert.Equal(["#5", "#1–#3"], SeriesGapFormatter.MissingParts([5, 1, 2, 3]));
        Assert.Equal(["#3", "#2", "#1"], SeriesGapFormatter.MissingParts([3, 2, 1]));
    }
}

[Trait("Category", "Unit")]
public class WishlistOrderingTests
{
    private static WishlistItemSnapshot Item(int id, string title, string author, string priority, int? seriesId = null, int? seriesOrder = null) =>
        new(id, title, author, priority, null, seriesId, seriesOrder, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void ByPriority_BucketsHighMediumLow_EachByTitleIgnoringCase()
    {
        var items = new[]
        {
            Item(1, "zebra", "A", "Low"),
            Item(2, "Beta", "A", "High"),
            Item(3, "alpha", "A", "High"),
            Item(4, "Mid", "A", "Medium"),
            Item(5, "Apple", "A", "Low"),
        };

        var groups = WishlistOrdering.ByPriority(items);

        Assert.Equal(["High", "Medium", "Low"], groups.Select(g => g.Label));
        Assert.Equal([3, 2], groups[0].Rows.Select(r => r.Id));
        Assert.Equal([4], groups[1].Rows.Select(r => r.Id));
        Assert.Equal([5, 1], groups[2].Rows.Select(r => r.Id));
    }

    [Fact]
    public void ByPriority_UnknownOrMiscasedPriority_FallsIntoLow()
    {
        var items = new[]
        {
            Item(1, "A", "X", "high"),    // case-sensitive match: not High
            Item(2, "B", "X", "Urgent"),
            Item(3, "C", "X", ""),
        };

        var group = Assert.Single(WishlistOrdering.ByPriority(items));
        Assert.Equal("Low", group.Label);
        Assert.Equal([1, 2, 3], group.Rows.Select(r => r.Id));
    }

    [Fact]
    public void ByPriority_EmptyBucketsAreLeftOut()
    {
        var group = Assert.Single(WishlistOrdering.ByPriority([Item(1, "A", "X", "Medium")]));
        Assert.Equal("Medium", group.Label);
        Assert.Empty(WishlistOrdering.ByPriority([]));
    }

    [Fact]
    public void ByAuthor_AuthorThenTitle_IgnoringCase()
    {
        var items = new[]
        {
            Item(1, "Second", "asimov", "Low"),
            Item(2, "Dune", "Herbert", "High"),
            Item(3, "first", "Asimov", "Medium"),
        };

        Assert.Equal([3, 1, 2], WishlistOrdering.ByAuthor(items).Select(i => i.Id));
    }

    [Fact]
    public void BySeries_SeriesMembersFirstInReadingOrder_StandalonesLastByTitle()
    {
        var items = new[]
        {
            Item(1, "Zed standalone", "A", "Low"),
            Item(2, "Series 9 #2", "A", "Low", seriesId: 9, seriesOrder: 2),
            Item(3, "Series 4 unordered", "A", "Low", seriesId: 4),
            Item(4, "Series 4 #1", "A", "Low", seriesId: 4, seriesOrder: 1),
            Item(5, "alpha standalone", "A", "Low"),
            Item(6, "Series 9 #1", "A", "Low", seriesId: 9, seriesOrder: 1),
            Item(7, "Orphan order", "A", "Low", seriesOrder: 1),
        };

        // Quirk pinned as-is: a standalone that carries a SeriesOrder (no
        // SeriesId) still sorts by that order, so it leads the standalones
        // instead of taking its title position.
        Assert.Equal([4, 3, 6, 2, 7, 5, 1], WishlistOrdering.BySeries(items).Select(i => i.Id));
    }
}

[Trait("Category", "Unit")]
public class SyncAgeFormatterTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FormatAge_Null_IsNever()
    {
        Assert.Equal("never", SyncAgeFormatter.FormatAge(null, Now));
        Assert.Equal("never", SyncAgeFormatter.FormatAge(null));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1m ago")]
    [InlineData(5 * 60 + 30, "5m ago")]
    [InlineData(59 * 60 + 59, "59m ago")]
    [InlineData(60 * 60, "1h ago")]
    [InlineData(23 * 3600 + 3599, "23h ago")]
    [InlineData(-30, "just now")] // a clock running ahead reads as just now
    public void FormatAge_RelativeLabels(int secondsAgo, string expected)
    {
        Assert.Equal(expected, SyncAgeFormatter.FormatAge(Now.AddSeconds(-secondsAgo), Now));
    }

    [Fact]
    public void FormatAge_ADayOrOlder_IsLocalTimestamp()
    {
        var synced = Now.AddHours(-24);
        Assert.Equal(synced.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), SyncAgeFormatter.FormatAge(synced, Now));
    }

    [Fact]
    public void FormatAge_WithoutNow_UsesTheClock()
    {
        Assert.Equal("just now", SyncAgeFormatter.FormatAge(DateTime.UtcNow));
        Assert.Equal("2h ago", SyncAgeFormatter.FormatAge(DateTime.UtcNow.AddMinutes(-150)));
    }
}
