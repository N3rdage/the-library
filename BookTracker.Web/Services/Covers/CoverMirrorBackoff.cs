namespace BookTracker.Web.Services.Covers;

// Retry spacing for a cover that failed to mirror: Initial after the first
// failed attempt, doubling with each further attempt, never more than Cap.
public static class CoverMirrorBackoff
{
    public static readonly TimeSpan Initial = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan Cap = TimeSpan.FromHours(24);

    /// <summary>
    /// Delay before the next attempt after <paramref name="attempts"/> failed
    /// attempts (values below 1 count as 1).
    /// </summary>
    public static TimeSpan Delay(int attempts)
    {
        var doublings = Math.Max(attempts, 1) - 1;
        // 2^11 minutes already exceeds the 24-hour cap; stopping there keeps
        // the shift well clear of overflow for any attempt count.
        if (doublings >= 11) return Cap;
        var delay = Initial * (1 << doublings);
        return delay < Cap ? delay : Cap;
    }
}
