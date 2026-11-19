namespace BookTracker.Application.Formatting;

/// <summary>
/// Date rules for loans. Loan dates are calendar dates (<see cref="DateOnly"/>),
/// so "today" has to be a calendar date in some zone. The app has no per-user
/// time zone, so <see cref="Today"/> uses the owner's zone,
/// <c>Australia/Melbourne</c>; <see cref="TimeZoneId"/> is the one place to
/// change it.
/// </summary>
public static class LoanDates
{
    /// <summary>IANA id of the zone "today" is taken in. Resolved through
    /// <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/>, which accepts
    /// IANA ids on Windows as well as Linux.</summary>
    public const string TimeZoneId = "Australia/Melbourne";

    private static readonly Lazy<TimeZoneInfo> Zone =
        new(() => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId));

    /// <summary>True from the day after <paramref name="dueOn"/>: a loan due today
    /// is not yet overdue. A loan with no due date is never overdue.</summary>
    public static bool IsOverdue(DateOnly? dueOn, DateOnly today) =>
        dueOn is { } due && today > due;

    /// <summary>The current calendar date in <see cref="TimeZoneId"/>.</summary>
    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone.Value).DateTime);
}
