using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Theming;

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

        var missingLabel = new Label
        {
            Text = "Missing " + FormatMissingOrders(gap.MissingOrders),
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        missingLabel.SetThemeColor(Label.TextColorProperty, "LeatherL", "LeatherD");

        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { nameLabel, progressLabel, missingLabel },
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

    // Compose "#2, #6" — orders prefixed with # so the row reads as
    // ordinals. Collapses runs of three or more (#2, #3, #4 → #2-#4)
    // so a long missing tail (a series the user is way behind on)
    // doesn't render as a wall of numbers.
    private static string FormatMissingOrders(IReadOnlyList<int> orders)
    {
        if (orders.Count == 0) return "(none)";

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
            parts.Add(end - start >= 2 ? $"#{start}-#{end}" : end == start ? $"#{start}" : $"#{start}, #{end}");
            i++;
        }
        return string.Join(", ", parts);
    }
}
