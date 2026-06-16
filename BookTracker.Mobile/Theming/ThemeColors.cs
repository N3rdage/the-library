namespace BookTracker.Mobile.Theming;

/// <summary>
/// Bridges the XAML design tokens (Colors.xaml) into imperatively-built
/// views. Pages that construct their UI in code-behind can't use
/// <c>AppThemeBinding</c> markup, so they call <see cref="SetThemeColor"/>
/// with token keys — the colours still live in exactly one place
/// (Colors.xaml), and the view switches automatically on light/dark.
///
/// This is the code-behind half of the TODO #37 palette centralisation: XAML
/// surfaces use <c>{AppThemeBinding Light=… Dark=…}</c> directly; code-behind
/// surfaces use these helpers. Neither re-hardcodes hex.
/// </summary>
public static class ThemeColors
{
    /// <summary>Resolves a <see cref="Color"/> token from the merged app
    /// resources by key (e.g. "TextL"). Throws if the key is missing — a
    /// typo'd token should fail loudly in dev, not render transparent.</summary>
    public static Color Get(string key) =>
        Application.Current?.Resources[key] as Color
        ?? throw new KeyNotFoundException($"Colour token '{key}' not found in app resources.");

    /// <summary>
    /// Applies a light/dark token pair to a bindable colour property, switching
    /// automatically with the system theme — the code-behind equivalent of
    /// <c>{AppThemeBinding Light={StaticResource lightKey}, Dark={StaticResource darkKey}}</c>.
    /// </summary>
    public static void SetThemeColor(
        this VisualElement view, BindableProperty property, string lightKey, string darkKey) =>
        view.SetAppThemeColor(property, Get(lightKey), Get(darkKey));
}
