using BookTracker.Mobile.Cache;
using Microsoft.Maui.Controls.Shapes;

namespace BookTracker.Mobile.Pages;

public partial class SeriesGapsPage : ContentPage
{
    private readonly ICatalogCache _cache;
    private bool _loaded;

    private static readonly Color Brass = Color.FromArgb("#A67B3A");
    private static readonly Color AgedParchment = Color.FromArgb("#F2EADB");
    private static readonly Color Ink = Color.FromArgb("#2C2416");
    private static readonly Color FadedInk = Color.FromArgb("#6B5D4A");
    private static readonly Color ErrorColor = Color.FromArgb("#9B3B2E");
    private static readonly Color Leather = Color.FromArgb("#6B2737");

    public SeriesGapsPage(ICatalogCache cache)
    {
        InitializeComponent();
        _cache = cache;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        await LoadGapsAsync();
    }

    private async Task LoadGapsAsync()
    {
        GapsLayout.Children.Clear();
        GapsLayout.Children.Add(new ActivityIndicator
        {
            IsRunning = true,
            Color = Leather,
            HorizontalOptions = LayoutOptions.Center,
        });

        IReadOnlyList<SeriesGap> gaps;
        try
        {
            gaps = await _cache.GetSeriesGapsAsync();
        }
        catch (Exception ex)
        {
            GapsLayout.Children.Clear();
            GapsLayout.Children.Add(new Label
            {
                Text = $"Couldn't load gaps: {ex.GetType().Name} — {ex.Message}",
                TextColor = ErrorColor,
                FontSize = 14,
                LineBreakMode = LineBreakMode.WordWrap,
            });
            return;
        }

        GapsLayout.Children.Clear();

        if (gaps.Count == 0)
        {
            // Empty-state surfaces both "no series at all" and "all
            // series you own are complete" with a single message —
            // both are good news.
            GapsLayout.Children.Add(new Label
            {
                Text = "No gaps in your series collection — either you're complete, or you haven't started any series with a known length yet.",
                FontSize = 14,
                TextColor = FadedInk,
                HorizontalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap,
                Margin = new Thickness(0, 16, 0, 0),
            });
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
            TextColor = Ink,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        var progressLabel = new Label
        {
            Text = $"{gap.OwnedCount} of {gap.ExpectedCount} owned",
            FontSize = 13,
            TextColor = FadedInk,
        };
        var missingLabel = new Label
        {
            Text = "Missing " + FormatMissingOrders(gap.MissingOrders),
            FontSize = 14,
            TextColor = Leather,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
        };

        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { nameLabel, progressLabel, missingLabel },
        };

        return new Border
        {
            Stroke = Brass,
            StrokeThickness = 1,
            BackgroundColor = AgedParchment,
            Padding = new Thickness(14, 12),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
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
