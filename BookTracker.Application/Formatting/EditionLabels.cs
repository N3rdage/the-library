using BookTracker.Data.Models;

namespace BookTracker.Application.Formatting;

public static class EditionLabels
{
    /// <summary>The edition as the Book Detail edition row names it: the format,
    /// then the ISBN ("no ISBN" when there is none).</summary>
    public static string For(BookFormat format, string? isbn) =>
        $"{format.DisplayName()} · {isbn ?? "no ISBN"}";
}
