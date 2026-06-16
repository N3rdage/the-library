using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Theming;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace BookTracker.Mobile.Pages;

public partial class SeriesGapsPage : ContentPage
{
    private readonly ICatalogCache _cache;

    public SeriesGapsPage(ICatalogCache cache)
    {
        InitializeComponent();
        _cache = cache;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _ = ((VisualElement)Content).InAsync(rise: 6); // tab cross-fade
        // Reload on every appearance. Under Shell this page is app-lifetime
        // (the singleton AppShell holds it), so a one-shot guard would leave
        // the gaps list stale after the catalog is refreshed on the Find tab.
        // GetSeriesGapsAsync is a single cached-table scan — cheap to re-run.
        await LoadGapsAsync();
    }

    private async void OnSyncClicked(object? sender, EventArgs e) =>
        await StatusSheetPage.OpenAsync(Navigation);

    private async Task LoadGapsAsync()
    {
        GapsLayout.Children.Clear();
        var spinner = new ActivityIndicator
        {
            IsRunning = true,
            HorizontalOptions = LayoutOptions.Center,
        };
        spinner.SetThemeColor(ActivityIndicator.ColorProperty, "LeatherL", "LeatherD");
        GapsLayout.Children.Add(spinner);

        IReadOnlyList<SeriesGap> gaps;
        try
        {
            gaps = await _cache.GetSeriesGapsAsync();
        }
        catch (Exception ex)
        {
            GapsLayout.Children.Clear();
            var error = new Label
            {
                Text = $"Couldn't load gaps: {ex.GetType().Name} — {ex.Message}",
                FontSize = 14,
                LineBreakMode = LineBreakMode.WordWrap,
            };
            error.SetThemeColor(Label.TextColorProperty, "MissTagTxL", "MissTagTxD");
            GapsLayout.Children.Add(error);
            return;
        }

        GapsLayout.Children.Clear();

        if (gaps.Count == 0)
        {
            // Empty-state surfaces both "no series at all" and "all
            // series you own are complete" with a single message —
            // both are good news.
            var empty = new Label
            {
                Text = "No gaps in your series collection — either you're complete, or you haven't started any series with a known length yet.",
                FontSize = 14,
                HorizontalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap,
                Margin = new Thickness(0, 16, 0, 0),
            };
            empty.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");
            GapsLayout.Children.Add(empty);
            return;
        }

        var summary = new Label
        {
            Text = gaps.Count == 1 ? "1 series with a gap" : $"{gaps.Count} series with gaps",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        summary.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");
        GapsLayout.Children.Add(summary);

        foreach (var gap in gaps)
        {
            GapsLayout.Children.Add(BuildGapCard(gap));
        }
    }

    private View BuildGapCard(SeriesGap gap)
    {
        var nameLabel = new Label
        {
            Text = gap.SeriesName,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        nameLabel.SetThemeColor(Label.TextColorProperty, "TextL", "TextD");

        var progressLabel = new Label
        {
            Text = $"{gap.OwnedCount} of {gap.ExpectedCount} owned",
            FontSize = 13,
        };
        progressLabel.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");

        // Owned/expected as a bar — green-leather fill on a BarTrack track.
        var bar = new ProgressBar
        {
            Progress = gap.ExpectedCount > 0
                ? Math.Clamp((double)gap.OwnedCount / gap.ExpectedCount, 0, 1)
                : 0,
            Margin = new Thickness(0, 2, 0, 4),
        };
        bar.SetThemeColor(ProgressBar.ProgressColorProperty, "GreenL", "GreenD");
        bar.SetThemeColor(ProgressBar.BackgroundColorProperty, "BarTrackL", "BarTrackD");

        var missingHeader = new Label
        {
            Text = "Missing",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
        };
        missingHeader.SetThemeColor(Label.TextColorProperty, "BrassTextL", "BrassTextD");

        var pills = new FlexLayout { Wrap = FlexWrap.Wrap };
        foreach (var part in MissingParts(gap.MissingOrders))
            pills.Add(MissingPill(part));

        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { nameLabel, progressLabel, bar, missingHeader, pills },
        };

        // Card style supplies the themed surface + border + 8 dp radius;
        // override padding to the gap card's 14,12 rung.
        return new Border
        {
            Style = (Style)Application.Current!.Resources["Card"],
            Padding = new Thickness(14, 12),
            Content = stack,
        };
    }

    // Surface chip, brass border + brass text — the design kit's "Series #N"
    // badge. One per missing slot, or a collapsed range for a long run.
    private static View MissingPill(string text)
    {
        var label = new Label { Text = text, FontSize = 12, FontAttributes = FontAttributes.Bold };
        label.SetThemeColor(Label.TextColorProperty, "BrassTextL", "BrassTextD");

        var border = new Border
        {
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 0, 6, 6),
            Stroke = new SolidColorBrush(ThemeColors.Get("Brass")), // Brass is mode-stable
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Content = label,
        };
        border.SetThemeColor(Border.BackgroundColorProperty, "SurfaceL", "SurfaceD");
        return border;
    }

    // Missing slots as pill captions: a single "#N" per slot, but a run of
    // three or more collapses to one "#2–#4" range pill so a series the user
    // is far behind on doesn't render as a wall of pills.
    private static List<string> MissingParts(IReadOnlyList<int> orders)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < orders.Count)
        {
            int start = orders[i];
            int end = start;
            while (i + 1 < orders.Count && orders[i + 1] == end + 1)
            {
                end = orders[i + 1];
                i++;
            }
            if (end - start + 1 >= 3)
                parts.Add($"#{start}–#{end}");
            else
                for (int n = start; n <= end; n++) parts.Add($"#{n}");
            i++;
        }
        return parts;
    }
}
