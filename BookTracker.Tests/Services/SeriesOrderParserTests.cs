using BookTracker.Application.Formatting;

namespace BookTracker.Tests.Services;

public class SeriesOrderParserTests
{
    [Theory]
    // Empty / whitespace → nothing stored.
    [InlineData(null, null, null)]
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    // Clean integers → int sort key, no display override.
    [InlineData("4", 4, null)]
    [InlineData(" 12 ", 12, null)]
    // Interquels → floor for sort, raw kept for display.
    [InlineData("4.5", 4, "4.5")]
    [InlineData("2.5", 2, "2.5")]
    // Hierarchical / suffixed positions → leading int for sort, raw for display.
    [InlineData("1A", 1, "1A")]
    [InlineData("10b", 10, "10b")]
    // No leading digit → no sort key, raw preserved.
    [InlineData("II", null, "II")]
    [InlineData("Prologue", null, "Prologue")]
    // Series order is 1-based: reject 0 / negatives outright (no slot, no label).
    [InlineData("0", null, null)]
    [InlineData("-5", null, null)]
    // A leading zero in a non-integer label keeps the label but no sort slot.
    [InlineData("0.5", null, "0.5")]
    // A leading run that overflows int leaves the sort key null, label preserved.
    [InlineData("99999999999", null, "99999999999")]
    public void Parse_SplitsOrderAndDisplay(string? raw, int? expectedOrder, string? expectedDisplay)
    {
        var (order, display) = SeriesOrderParser.Parse(raw);
        Assert.Equal(expectedOrder, order);
        Assert.Equal(expectedDisplay, display);
    }

    [Theory]
    // Display override wins when present.
    [InlineData(4, "4.5", "4.5")]
    [InlineData(null, "II", "II")]
    // Otherwise the integer renders.
    [InlineData(4, null, "4")]
    // Neither set → null.
    [InlineData(null, null, null)]
    public void Format_PrefersDisplayThenInteger(int? order, string? display, string? expected)
    {
        Assert.Equal(expected, SeriesOrderParser.Format(order, display));
    }

    [Fact]
    public void Parse_RoundTripsThroughFormat_ForInterquel()
    {
        // Capture "4.5" → store (4, "4.5") → the edit form re-renders "4.5".
        var (order, display) = SeriesOrderParser.Parse("4.5");
        Assert.Equal("4.5", SeriesOrderParser.Format(order, display));
    }
}
