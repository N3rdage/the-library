using MudBlazor;

namespace BookTracker.Web.Theme;

// Warm "library" palette — leather spines, brass fixtures, parchment pages, ink text.
// Applied via <MudThemeProvider Theme="BookTrackerTheme.Default" /> in MainLayout.
public static class BookTrackerTheme
{
    public static readonly MudTheme Default = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#6B2737",
            PrimaryContrastText = "#FAF6EC",
            Secondary = "#A67B3A",
            SecondaryContrastText = "#2C2416",
            Tertiary = "#3D5A3F",
            TertiaryContrastText = "#FAF6EC",

            Background = "#FAF6EC",
            BackgroundGray = "#F2EADB",
            Surface = "#F2EADB",

            AppbarBackground = "#3E2723",
            AppbarText = "#F2EADB",

            DrawerBackground = "#F2EADB",
            DrawerText = "#2C2416",
            DrawerIcon = "#6B5D4A",

            TextPrimary = "#2C2416",
            TextSecondary = "#6B5D4A",
            TextDisabled = "rgba(44,36,22,0.38)",

            ActionDefault = "#6B5D4A",
            ActionDisabled = "rgba(44,36,22,0.26)",
            ActionDisabledBackground = "rgba(44,36,22,0.12)",

            Divider = "rgba(44,36,22,0.12)",
            DividerLight = "rgba(44,36,22,0.06)",
            LinesDefault = "rgba(44,36,22,0.12)",
            LinesInputs = "rgba(44,36,22,0.42)",
            TableLines = "rgba(44,36,22,0.12)",
            TableStriped = "rgba(107,39,55,0.04)",
            TableHover = "rgba(107,39,55,0.08)",

            // Muted status colours so they sit inside the palette rather than shouting over it.
            Success = "#4F6B3D",
            Info = "#3A6B7A",
            Warning = "#B8861B",
            Error = "#9B3B2E",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Roboto", "Helvetica", "Arial", "sans-serif" },
            },
        },
    };
}
