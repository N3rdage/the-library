using BookTracker.Web.Services.Covers;

namespace BookTracker.Tests.Services.Covers;

[Trait("Category", TestCategories.Unit)]
public class CoverMirrorBackoffTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    public void Delay_DoublesFromOneMinute(int attempts, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), CoverMirrorBackoff.Delay(attempts));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Delay_NonPositiveAttempts_CountAsOne(int attempts)
    {
        Assert.Equal(CoverMirrorBackoff.Initial, CoverMirrorBackoff.Delay(attempts));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(40)]
    [InlineData(int.MaxValue)]
    public void Delay_LargeAttempts_HitTheCap(int attempts)
    {
        Assert.Equal(CoverMirrorBackoff.Cap, CoverMirrorBackoff.Delay(attempts));
    }

    [Fact]
    public void Constants_AreOneMinuteAndTwentyFourHours()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), CoverMirrorBackoff.Initial);
        Assert.Equal(TimeSpan.FromHours(24), CoverMirrorBackoff.Cap);
    }
}
