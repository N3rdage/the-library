using System.Text.RegularExpressions;

namespace BookTracker.Application.Shelf;

// The shelving order inside one genre group on /shelf-order: lead-author
// surname (then the full name, so two Smiths stay apart), then series name and
// series order, then first-published date, then title. Culture-invariant and
// case-insensitive. A missing value sorts after a present one at every step, so
// an author's series run together ahead of their standalones, and books with no
// author close the group.
public sealed partial record ShelfOrderKey(
    string? Surname,
    string? Author,
    string? Series,
    int? SeriesOrder,
    DateOnly? FirstPublished,
    string Title) : IComparable<ShelfOrderKey>
{
    private static readonly StringComparer Text = StringComparer.InvariantCultureIgnoreCase;

    public static ShelfOrderKey For(
        string? leadAuthor, string? series, int? seriesOrder, DateOnly? firstPublished, string title)
    {
        var author = string.IsNullOrWhiteSpace(leadAuthor) ? null : leadAuthor.Trim();
        var seriesName = string.IsNullOrWhiteSpace(series) ? null : series.Trim();
        return new ShelfOrderKey(
            author is null ? null : SurnameOf(author), author, seriesName, seriesOrder, firstPublished, title.Trim());
    }

    // The part before a comma ("Tolkien, J. R. R."), else the last word
    // ("J. R. R. Tolkien"). A last word that is only initials ("Iain M.") is
    // no surname, so the whole name stands in.
    public static string SurnameOf(string name)
    {
        var trimmed = name.Trim();
        var comma = trimmed.IndexOf(',', StringComparison.Ordinal);
        if (comma > 0)
            return trimmed[..comma].Trim();

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return trimmed;
        var last = words[^1];
        return InitialsRegex().IsMatch(last) ? trimmed : last;
    }

    // "Surname, Rest" for the shelf list ("Tolkien, J. R. R."); a name already
    // written surname-first, or one with no usable surname, reads as entered.
    public static string ShelvedAs(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Contains(',', StringComparison.Ordinal))
            return trimmed;
        var surname = SurnameOf(trimmed);
        if (surname.Length == trimmed.Length)
            return trimmed;
        var rest = trimmed[..^surname.Length].Trim();
        return $"{surname}, {rest}";
    }

    public int CompareTo(ShelfOrderKey? other)
    {
        if (other is null) return -1;
        var c = CompareText(Surname, other.Surname);
        if (c != 0) return c;
        c = CompareText(Author, other.Author);
        if (c != 0) return c;
        c = CompareText(Series, other.Series);
        if (c != 0) return c;
        c = CompareNullable(SeriesOrder, other.SeriesOrder);
        if (c != 0) return c;
        c = CompareNullable(FirstPublished, other.FirstPublished);
        if (c != 0) return c;
        return Text.Compare(Title, other.Title);
    }

    private static int CompareText(string? a, string? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => 1,
        (_, null) => -1,
        _ => Text.Compare(a, b),
    };

    private static int CompareNullable<T>(T? a, T? b) where T : struct, IComparable<T> => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => 1,
        (_, null) => -1,
        _ => a.Value.CompareTo(b.Value),
    };

    // "M", "M.", "J.R.R." or "J.R.R" — a word made only of single-letter initials.
    [GeneratedRegex(@"^\p{L}(\.\p{L})*\.?$")]
    private static partial Regex InitialsRegex();
}
