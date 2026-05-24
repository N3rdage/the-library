using BookTracker.Shared.Formatting;

namespace BookTracker.Mobile.Cache.Tests;

// Lives in this project because BookTracker.Shared has no test project
// of its own (Shared is referenced transitively via Mobile.Cache).
[Trait("Category", "Unit")]
public class OrdinalFormatterTests
{
    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(10, "10th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(14, "14th")]
    [InlineData(20, "20th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    [InlineData(24, "24th")]
    [InlineData(100, "100th")]
    [InlineData(101, "101st")]
    [InlineData(111, "111th")]
    [InlineData(112, "112th")]
    [InlineData(113, "113th")]
    [InlineData(121, "121st")]
    public void Ordinal_HandlesTeensAndUnits(int n, string expected)
    {
        Assert.Equal(expected, OrdinalFormatter.Ordinal(n));
    }

    [Fact]
    public void OrdinalEdition_AppendsEdSuffix()
    {
        Assert.Equal("3rd ed.", OrdinalFormatter.OrdinalEdition(3));
        Assert.Equal("11th ed.", OrdinalFormatter.OrdinalEdition(11));
    }
}
