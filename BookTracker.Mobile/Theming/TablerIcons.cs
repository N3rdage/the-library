namespace BookTracker.Mobile.Theming;

/// <summary>
/// Glyph strings for the Tabler icon webfont (bundled as
/// Resources/Fonts/tabler-icons.ttf, registered as the "Tabler" font family).
/// Use with a <see cref="FontImageSource"/>:
/// <code>new FontImageSource { FontFamily = "Tabler", Glyph = TablerIcons.Search }</code>
///
/// Values are the Tabler v3 webfont's Private-Use-Area codepoints (MIT),
/// written as hex ints rather than \u escapes so the (invisible) glyph chars
/// stay unambiguous in source. To add an icon, look up its <c>\eXXXX</c> value
/// in the upstream <c>tabler-icons.css</c> and add a line — the full font is
/// bundled, so any glyph is available.
/// </summary>
public static class TablerIcons
{
    private static string G(int codepoint) => char.ConvertFromUtf32(codepoint);

    public static readonly string Search = G(0xEB1C);
    public static readonly string Barcode = G(0xEBC6);
    public static readonly string Star = G(0xEB2E);
    public static readonly string Books = G(0xEFF2);
    public static readonly string Book = G(0xEA39);
    public static readonly string User = G(0xEB4D);
    public static readonly string Refresh = G(0xEB13);
    public static readonly string Wifi = G(0xEB52);
    public static readonly string WifiOff = G(0xECFA);
    public static readonly string ArrowsSort = G(0xEB5A);
    public static readonly string ChevronRight = G(0xEA61);
    public static readonly string ChevronLeft = G(0xEA60);
    public static readonly string Logout = G(0xEBA8);
    public static readonly string Plus = G(0xEB0B);
    public static readonly string X = G(0xEB55);
    public static readonly string DotsVertical = G(0xEA94);
}
