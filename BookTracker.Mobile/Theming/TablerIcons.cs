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

    // Only the three tab-bar glyphs are in use — the redesign settled on text
    // and emoji affordances elsewhere. The full font is bundled, so add a line
    // here (with its upstream \eXXXX codepoint) when a surface actually needs one.
    public static readonly string Search = G(0xEB1C);
    public static readonly string Star = G(0xEB2E);
    public static readonly string Books = G(0xEFF2);
}
