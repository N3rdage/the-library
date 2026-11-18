namespace BookTracker.Mobile.Cache.Presentation;

// Display name for an edition's wire-format BookFormat string on
// Bookshelf's Result page.
public static class EditionFormatName
{
    // "MassMarketPaperback" → "Mass Market Paperback": a space before each
    // upper-case letter that follows a non-upper-case one.
    public static string FormatPrettyName(string format)
    {
        if (string.IsNullOrEmpty(format)) return "(unknown)";
        var sb = new System.Text.StringBuilder(format.Length + 4);
        for (int i = 0; i < format.Length; i++)
        {
            if (i > 0 && char.IsUpper(format[i]) && !char.IsUpper(format[i - 1])) sb.Append(' ');
            sb.Append(format[i]);
        }
        return sb.ToString();
    }
}
