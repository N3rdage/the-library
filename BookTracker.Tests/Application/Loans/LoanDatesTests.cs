using BookTracker.Application.Formatting;
using Xunit;

namespace BookTracker.Tests;

[Trait("Category", TestCategories.Unit)]
public class LoanDatesTests
{
    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static readonly DateOnly Due = new(2026, 11, 20);

    [Fact]
    public void IsOverdue_noDueDate_isFalse() =>
        Assert.False(LoanDates.IsOverdue(null, new DateOnly(2030, 1, 1)));

    [Fact]
    public void IsOverdue_beforeDueDate_isFalse() =>
        Assert.False(LoanDates.IsOverdue(Due, Due.AddDays(-1)));

    [Fact]
    public void IsOverdue_onDueDate_isFalse() =>
        Assert.False(LoanDates.IsOverdue(Due, Due));

    [Fact]
    public void IsOverdue_dayAfterDueDate_isTrue() =>
        Assert.True(LoanDates.IsOverdue(Due, Due.AddDays(1)));

    [Fact]
    public void Today_isMelbourneDate_aheadOfUtc()
    {
        // 14:00 UTC on 19 Nov is 01:00 on 20 Nov in Melbourne (AEDT, UTC+11).
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 19, 14, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 11, 20), LoanDates.Today(clock));
    }

    [Fact]
    public void Today_isMelbourneDate_sameAsUtc()
    {
        // 12:00 UTC on 19 Nov is 23:00 on 19 Nov in Melbourne.
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 11, 19), LoanDates.Today(clock));
    }

    [Fact]
    public void Today_followsStandardTimeInWinter()
    {
        // June is AEST (UTC+10): 13:59 UTC is 23:59 the same day, 14:00 UTC is midnight.
        Assert.Equal(new DateOnly(2026, 6, 15),
            LoanDates.Today(new FixedClock(new DateTimeOffset(2026, 6, 15, 13, 59, 0, TimeSpan.Zero))));
        Assert.Equal(new DateOnly(2026, 6, 16),
            LoanDates.Today(new FixedClock(new DateTimeOffset(2026, 6, 15, 14, 0, 0, TimeSpan.Zero))));
    }
}
