using BookTracker.Data;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Pure domain unit tests for the WishlistItem aggregate — no EF, no container.
// Covers the Create factory, the CreateSeriesSlot stub (which keeps its series
// link), and SetIsbns' dual-write: the legacy Isbn column is the primary of a
// trimmed, case-insensitively de-duplicated Isbns list.
[Trait("Category", TestCategories.Unit)]
public class WishlistItemAggregateTests
{
    [Fact]
    public void Create_appliesDetails_andTrims()
    {
        var item = WishlistItem.Create(
            "  Anathem  ", "  Neal Stephenson  ", WishlistPriority.High,
            ["9780061474095"], "  https://covers.example/anathem.jpg  ");

        Assert.Equal("Anathem", item.Title);
        Assert.Equal("Neal Stephenson", item.Author);
        Assert.Equal(WishlistPriority.High, item.Priority);
        Assert.Equal("https://covers.example/anathem.jpg", item.CoverUrl);
        Assert.Equal("9780061474095", item.Isbn);
        Assert.Equal(["9780061474095"], item.Isbns.Select(i => i.Isbn));
        Assert.Null(item.SeriesId);
        Assert.Null(item.SeriesOrder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_blankAuthor_becomesUnknown(string? author)
    {
        var item = WishlistItem.Create("Mort", author, WishlistPriority.Medium, [], null);

        Assert.Equal("Unknown", item.Author);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_blankTitle_throws(string? title)
    {
        Assert.Throws<DomainRuleException>(() =>
            WishlistItem.Create(title, "Someone", WishlistPriority.Medium, [], null));
    }

    [Fact]
    public void Create_blankCoverUrl_andNoIsbns_leaveThemEmpty()
    {
        var item = WishlistItem.Create("Mort", "Terry Pratchett", WishlistPriority.Low, [], "   ");

        Assert.Null(item.CoverUrl);
        Assert.Null(item.Isbn);
        Assert.Empty(item.Isbns);
    }

    [Fact]
    public void CreateSeriesSlot_keepsItsSeriesLink_andTitlesTheSlot()
    {
        var item = WishlistItem.CreateSeriesSlot(42, "Foundation", 3, "  Isaac Asimov  ");

        Assert.Equal(42, item.SeriesId);
        Assert.Equal(3, item.SeriesOrder);
        Assert.Equal("Foundation #3", item.Title);
        Assert.Equal("Isaac Asimov", item.Author);
        Assert.Equal(WishlistPriority.Medium, item.Priority);
        Assert.Null(item.Isbn);
        Assert.Empty(item.Isbns);
        Assert.Null(item.CoverUrl);
    }

    [Fact]
    public void CreateSeriesSlot_blankSeriesAuthor_becomesUnknown()
    {
        var item = WishlistItem.CreateSeriesSlot(7, "Mystery Series", 1, "  ");

        Assert.Equal("Unknown", item.Author);
    }

    [Fact]
    public void SetIsbns_trimsDropsBlanksAndDeduplicatesCaseInsensitively_firstIsPrimary()
    {
        var item = WishlistItem.Create("Snow Crash", "Neal Stephenson", WishlistPriority.Medium, [], null);

        item.SetIsbns(["  ", " 055338095x ", "055338095X", "", "9780553380958", " 9780553380958"]);

        Assert.Equal("055338095x", item.Isbn);
        Assert.Equal(["055338095x", "9780553380958"], item.Isbns.Select(i => i.Isbn));
    }

    [Fact]
    public void SetIsbns_keepsHyphensAsGiven()
    {
        var item = WishlistItem.Create("Snow Crash", null, WishlistPriority.Medium, [], null);

        item.SetIsbns(["978-0-553-38095-8"]);

        Assert.Equal("978-0-553-38095-8", item.Isbn);
        Assert.Equal(["978-0-553-38095-8"], item.Isbns.Select(i => i.Isbn));
    }

    [Fact]
    public void SetIsbns_replacesThePreviousSet()
    {
        var item = WishlistItem.Create("Eric", null, WishlistPriority.Medium, ["111", "222"], null);

        item.SetIsbns(["333"]);

        Assert.Equal("333", item.Isbn);
        Assert.Equal(["333"], item.Isbns.Select(i => i.Isbn));
    }

    [Fact]
    public void SetIsbns_emptyOrNull_clearsBoth()
    {
        var item = WishlistItem.Create("Eric", null, WishlistPriority.Medium, ["111"], null);

        item.SetIsbns([]);
        Assert.Null(item.Isbn);
        Assert.Empty(item.Isbns);

        item.SetIsbns(["111"]);
        item.SetIsbns(null!);
        Assert.Null(item.Isbn);
        Assert.Empty(item.Isbns);
    }
}
