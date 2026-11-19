namespace BookTracker.Mobile.Cache.Presentation;

// Relative-time label for Bookshelf's last-synced timestamp (status sheet
// + the Find tab's sync chip).
public static class SyncAgeFormatter
{
    /// <summary>Relative-time label for a last-synced timestamp:
    /// "never" / "just now" / "5m ago" / "2h ago" / "2026-05-12 14:23".</summary>
    public static string FormatAge(DateTime? syncedUtc) => FormatAge(syncedUtc, DateTime.UtcNow);

    /// <summary>Same as <see cref="FormatAge(DateTime?)"/> against an explicit
    /// "now" so tests can pin the boundaries.</summary>
    public static string FormatAge(DateTime? syncedUtc, DateTime nowUtc)
    {
        if (syncedUtc is not { } utc) return "never";
        var ago = nowUtc - utc;
        if (ago.TotalMinutes < 1) return "just now";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24) return $"{(int)ago.TotalHours}h ago";
        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
