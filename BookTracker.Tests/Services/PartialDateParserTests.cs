using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

[Trait("Category", TestCategories.Unit)]
public class PartialDateParserTests
{
    [Theory]
    [InlineData("1973", 1973, 1, 1, DatePrecision.Year)]
    [InlineData("1934", 1934, 1, 1, DatePrecision.Year)]
    public void TryParse_YearOnly(string input, int y, int m, int d, DatePrecision precision)
    {
        var result = PartialDateParser.TryParse(input);
        Assert.NotNull(result);
        Assert.Equal(new DateOnly(y, m, d), result!.Date);
        Assert.Equal(precision, result.Precision);
    }

    [Theory]
    [InlineData("1973-10", 1973, 10, 1, DatePrecision.Month)]
    [InlineData("10/1973", 1973, 10, 1, DatePrecision.Month)]
    [InlineData("1/2024", 2024, 1, 1, DatePrecision.Month)]
    [InlineData("Oct 1973", 1973, 10, 1, DatePrecision.Month)]
    [InlineData("October 1973", 1973, 10, 1, DatePrecision.Month)]
    public void TryParse_MonthYear(string input, int y, int m, int d, DatePrecision precision)
    {
        var result = PartialDateParser.TryParse(input);
        Assert.NotNull(result);
        Assert.Equal(new DateOnly(y, m, d), result!.Date);
        Assert.Equal(precision, result.Precision);
    }

    [Theory]
    [InlineData("1973-10-12", 1973, 10, 12)]
    [InlineData("12 Oct 1973", 1973, 10, 12)]
    [InlineData("12 October 1973", 1973, 10, 12)]
    public void TryParse_FullDate(string input, int y, int m, int d)
    {
        var result = PartialDateParser.TryParse(input);
        Assert.NotNull(result);
        Assert.Equal(new DateOnly(y, m, d), result!.Date);
        Assert.Equal(DatePrecision.Day, result.Precision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_BlankInput_ReturnsEmptyPartialDate(string? input)
    {
        var result = PartialDateParser.TryParse(input);
        Assert.NotNull(result);
        Assert.Null(result!.Date);
        Assert.Equal(DatePrecision.Day, result.Precision);
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("13/2024")]   // invalid month
    [InlineData("3000")]       // year out of range
    [InlineData("0099")]       // year out of range
    [InlineData("Octobre 1973")] // wrong language
    public void TryParse_GarbageInput_ReturnsNull(string input)
    {
        Assert.Null(PartialDateParser.TryParse(input));
    }

    [Theory]
    [InlineData(2024, 3, 15, DatePrecision.Day, "15 Mar 2024")]
    [InlineData(1973, 10, 1, DatePrecision.Month, "Oct 1973")]
    [InlineData(1934, 1, 1, DatePrecision.Year, "1934")]
    public void Format_RendersAccordingToPrecision(int y, int m, int d, DatePrecision precision, string expected)
    {
        Assert.Equal(expected, PartialDateParser.Format(new DateOnly(y, m, d), precision));
    }

    [Fact]
    public void Format_NullDate_ReturnsEmpty()
    {
        Assert.Equal("", PartialDateParser.Format(null, DatePrecision.Day));
    }

    [Fact]
    public void RoundTrip_YearText_PreservesYearPrecision()
    {
        var parsed = PartialDateParser.TryParse("1934");
        Assert.NotNull(parsed);
        var formatted = PartialDateParser.Format(parsed!.Date, parsed.Precision);
        Assert.Equal("1934", formatted);
    }

    [Fact]
    public void RoundTrip_MonthText_PreservesMonthPrecision()
    {
        var parsed = PartialDateParser.TryParse("Oct 1973");
        Assert.NotNull(parsed);
        var formatted = PartialDateParser.Format(parsed!.Date, parsed.Precision);
        Assert.Equal("Oct 1973", formatted);
    }
}
