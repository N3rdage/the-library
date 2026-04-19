using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

public class BookFormatNormalizerTests
{
    [Theory]
    [InlineData("Hardcover", BookFormat.Hardcover)]
    [InlineData("Hardback", BookFormat.Hardcover)]
    [InlineData("Hard cover", BookFormat.Hardcover)]
    [InlineData("Paperback", BookFormat.TradePaperback)]
    [InlineData("Trade paperback", BookFormat.TradePaperback)]
    [InlineData("Softcover", BookFormat.TradePaperback)]
    [InlineData("Mass Market Paperback", BookFormat.MassMarketPaperback)]
    [InlineData("MASS MARKET", BookFormat.MassMarketPaperback)]
    [InlineData("Large Print", BookFormat.LargePrint)]
    [InlineData("Large Type", BookFormat.LargePrint)]
    public void Normalize_MapsPhysicalFormatString(string input, BookFormat expected)
    {
        Assert.Equal(expected, BookFormatNormalizer.Normalize(input, null));
    }

    [Fact]
    public void Normalize_LargePrintWinsOverHardcoverWhenBothMentioned()
    {
        Assert.Equal(BookFormat.LargePrint, BookFormatNormalizer.Normalize("Large Print Hardcover", null));
    }

    [Fact]
    public void Normalize_MassMarketWinsOverGenericPaperback()
    {
        Assert.Equal(BookFormat.MassMarketPaperback, BookFormatNormalizer.Normalize("Mass Market Paperback", null));
    }

    [Theory]
    [InlineData("6.9 x 4.2 x 1 inches")]
    [InlineData("17.5 x 10.6 x 2.5 cm")]
    public void Normalize_DimensionsInferMassMarket(string dims)
    {
        // Both sets of dimensions are within the mass-market envelope.
        Assert.Equal(BookFormat.MassMarketPaperback, BookFormatNormalizer.Normalize(null, dims));
    }

    [Fact]
    public void Normalize_TradeSizedDimensionsReturnNull()
    {
        // Dimensions alone can't disambiguate trade paperback from hardcover,
        // so we conservatively return null and let callers keep their default.
        Assert.Null(BookFormatNormalizer.Normalize(null, "9.0 x 6.0 x 1.5 inches"));
    }

    [Fact]
    public void Normalize_ReturnsNullWhenBothInputsAreEmpty()
    {
        Assert.Null(BookFormatNormalizer.Normalize(null, null));
        Assert.Null(BookFormatNormalizer.Normalize("", "  "));
    }

    [Fact]
    public void Normalize_ReturnsNullForUnrecognisedFormatString()
    {
        Assert.Null(BookFormatNormalizer.Normalize("Audiobook", null));
    }

    [Fact]
    public void Normalize_PhysicalFormatStringTakesPrecedenceOverDimensions()
    {
        // String says hardcover, dims say mass-market — string wins.
        Assert.Equal(BookFormat.Hardcover, BookFormatNormalizer.Normalize("Hardcover", "6.9 x 4.2 x 1 inches"));
    }
}
