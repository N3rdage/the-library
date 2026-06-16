namespace BookTracker.Mobile.Theming;

/// <summary>
/// Element-level entrance motion for the redesign (brief §9). Principle: motion
/// <i>decorates</i> the result, it never gates it — every helper short-circuits
/// to the final visual state instantly when <see cref="Enabled"/> is false, so a
/// fast tap (or a reduced-motion device) still feels instant.
///
/// Shell page push/pop transitions aren't very customisable in MAUI, so these
/// element entrances are the deliberate route. <see cref="Enabled"/> is set once
/// at startup from the Android animator-duration-scale (0 ⇒ reduced motion).
/// </summary>
public static class Motion
{
    public static bool Enabled = true;

    /// <summary>Fade + rise a container in as its data binds. Durations stay in
    /// the 150–220 ms decelerating band.</summary>
    public static Task InAsync(this VisualElement v, uint ms = 190, double rise = 12)
    {
        if (!Enabled) { v.Opacity = 1; v.TranslationY = 0; return Task.CompletedTask; }
        v.Opacity = 0;
        v.TranslationY = rise;
        return Task.WhenAll(
            v.FadeToAsync(1, ms, Easing.CubicOut),
            v.TranslateToAsync(0, 0, ms, Easing.CubicOut));
    }

    /// <summary>A quick scale pulse — used as the "barcode recognised" cue on the
    /// scan reticle. No-op (and no delay) under reduced motion.</summary>
    public static async Task PulseAsync(this VisualElement v, double scale = 1.06, uint up = 90, uint down = 110)
    {
        if (!Enabled) return;
        await v.ScaleToAsync(scale, up, Easing.CubicOut);
        await v.ScaleToAsync(1.0, down, Easing.CubicIn);
    }
}
