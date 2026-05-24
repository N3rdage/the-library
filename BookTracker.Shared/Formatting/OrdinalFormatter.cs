namespace BookTracker.Shared.Formatting;

// Tiny presentation helper. Lives in Shared so both Web (Detail.razor's
// edition row) and Mobile (ScanPage's edition row) consume the same
// implementation — the alternative was two private copies that could
// drift on edge cases (the 11/12/13 special case is easy to omit).
//
// Pure ordinal logic, no DTO dependency. Tested from
// BookTracker.Mobile.Cache.Tests because Shared has no test project
// of its own (and Shared is a Mobile.Cache transitive ref anyway).
public static class OrdinalFormatter
{
    /// <summary>English ordinal suffix for the given positive integer:
    /// "1st" / "2nd" / "3rd" / "4th" / ... "11th" / "12th" / "13th" /
    /// ... "21st" / "22nd" / "23rd" / ... etc.</summary>
    public static string Ordinal(int n)
    {
        var mod100 = n % 100;
        var suffix = (mod100 is >= 11 and <= 13) ? "th" : (n % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
        return $"{n}{suffix}";
    }

    /// <summary>Convenience: "{ordinal} ed." — e.g. "3rd ed.".</summary>
    public static string OrdinalEdition(int n) => $"{Ordinal(n)} ed.";
}
