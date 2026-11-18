using BookTracker.Application.Shelf;

namespace BookTracker.Tests;

[Trait("Category", TestCategories.Unit)]
public class ShelfOrderKeyTests
{
    [Theory]
    [InlineData("J. R. R. Tolkien", "Tolkien")]
    [InlineData("Tolkien, J. R. R.", "Tolkien")]
    [InlineData("  Iain M. Banks ", "Banks")]
    [InlineData("Plato", "Plato")]
    [InlineData("Iain M.", "Iain M.")]
    [InlineData("J.R.R.", "J.R.R.")]
    public void SurnameOf_TakesTheCommaPartElseTheLastWordElseTheWholeName(string name, string expected)
        => Assert.Equal(expected, ShelfOrderKey.SurnameOf(name));

    [Theory]
    [InlineData("Terry Pratchett", "Pratchett, Terry")]
    [InlineData("Pratchett, Terry", "Pratchett, Terry")]
    [InlineData("Plato", "Plato")]
    [InlineData("Iain M.", "Iain M.")]
    public void ShelvedAs_PutsTheSurnameFirst(string name, string expected)
        => Assert.Equal(expected, ShelfOrderKey.ShelvedAs(name));

    [Fact]
    public void Sort_BySurnameThenSeriesAndOrderThenDateThenTitle()
    {
        var keys = new[]
        {
            ShelfOrderKey.For("Terry Pratchett", null, null, new DateOnly(1990, 1, 1), "Good Omens"),
            ShelfOrderKey.For("Terry Pratchett", "Discworld", 2, new DateOnly(1986, 1, 1), "The Light Fantastic"),
            ShelfOrderKey.For("Isaac Asimov", "Foundation", 1, null, "Foundation"),
            ShelfOrderKey.For("Terry Pratchett", "Discworld", 1, new DateOnly(1983, 1, 1), "The Colour of Magic"),
            ShelfOrderKey.For("Terry Pratchett", null, null, new DateOnly(1971, 1, 1), "The Carpet People"),
            ShelfOrderKey.For(null, null, null, null, "Anonymous Tales"),
            ShelfOrderKey.For("Terry Pratchett", null, null, null, "A Slip of the Keyboard"),
        };

        var titles = keys.Order().Select(k => k.Title).ToList();

        Assert.Equal(
            ["Foundation", "The Colour of Magic", "The Light Fantastic", "The Carpet People", "Good Omens",
             "A Slip of the Keyboard", "Anonymous Tales"],
            titles);
    }

    [Fact]
    public void Sort_IsCaseInsensitive()
    {
        var keys = new[]
        {
            ShelfOrderKey.For("ann leckie", null, null, null, "b"),
            ShelfOrderKey.For("Ann Leckie", null, null, null, "A"),
            ShelfOrderKey.For("Iain Banks", null, null, null, "z"),
        };

        Assert.Equal(["z", "A", "b"], keys.Order().Select(k => k.Title).ToList());
    }

    [Fact]
    public void Sort_SameSurname_KeepsEachAuthorTogether()
    {
        var keys = new[]
        {
            ShelfOrderKey.For("Zadie Smith", null, null, null, "A"),
            ShelfOrderKey.For("Ali Smith", null, null, null, "B"),
            ShelfOrderKey.For("Zadie Smith", null, null, null, "C"),
        };

        Assert.Equal(["B", "A", "C"], keys.Order().Select(k => k.Title).ToList());
    }
}
