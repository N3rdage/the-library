using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Services;
using BookTracker.Mobile.Theming;
using BookTracker.Shared.Wishlist;
using Microsoft.Maui.Controls.Shapes;

namespace BookTracker.Mobile.Pages;

public partial class WishlistPage : ContentPage
{
    private readonly ICatalogCache _cache;
    private readonly IApiClient _api;
    private readonly IHttpClientFactory _httpFactory;

    private IReadOnlyList<WishlistItemSnapshot> _items = [];
    private enum Sort { Priority, Author, Series }
    private Sort _sort = Sort.Priority;

    public WishlistPage(ICatalogCache cache, IApiClient api, IHttpClientFactory httpFactory)
    {
        InitializeComponent();
        _cache = cache;
        _api = api;
        _httpFactory = httpFactory;
        UpdateSortButtons();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _ = ((VisualElement)Content).InAsync(rise: 6); // tab cross-fade
        await LoadFromCacheAsync();
    }

    private async void OnSyncClicked(object? sender, EventArgs e) =>
        await StatusSheetPage.OpenAsync(Navigation);

    private async Task LoadFromCacheAsync()
    {
        try
        {
            _items = await _cache.GetWishlistAsync();
            Render();
        }
        catch (Exception ex)
        {
            _items = [];
            SortSegment.IsVisible = false;
            StatusLabel.Text = $"Couldn't read cached wishlist: {ex.GetType().Name}";
        }
    }

    private void OnSortPriority(object? sender, EventArgs e) { _sort = Sort.Priority; UpdateSortButtons(); Render(); }
    private void OnSortAuthor(object? sender, EventArgs e) { _sort = Sort.Author; UpdateSortButtons(); Render(); }
    private void OnSortSeries(object? sender, EventArgs e) { _sort = Sort.Series; UpdateSortButtons(); Render(); }

    private void Render()
    {
        ItemsLayout.Children.Clear();
        SortSegment.IsVisible = _items.Count > 0;

        if (_items.Count == 0)
        {
            StatusLabel.Text = "Wishlist empty. Tap Refresh wishlist to pull from the server, or add books from Bookcase web.";
            return;
        }
        StatusLabel.Text = $"{_items.Count} {(_items.Count == 1 ? "book" : "books")} on your wishlist.";

        switch (_sort)
        {
            case Sort.Priority:
                // High → Medium → Low(/other) buckets, each titled, rows by title.
                foreach (var (label, inBucket) in PriorityBuckets)
                {
                    var rows = _items.Where(inBucket)
                        .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (rows.Count == 0) continue;
                    ItemsLayout.Children.Add(GroupHeader(label));
                    foreach (var it in rows) ItemsLayout.Children.Add(BuildItemRow(it));
                }
                break;

            case Sort.Author:
                foreach (var it in _items
                    .OrderBy(i => i.Author, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase))
                    ItemsLayout.Children.Add(BuildItemRow(it));
                break;

            case Sort.Series:
                // Series members first, grouped by series and in reading order;
                // standalone wishlist entries fall to the end, by title.
                foreach (var it in _items
                    .OrderBy(i => i.SeriesId is null)
                    .ThenBy(i => i.SeriesId ?? int.MaxValue)
                    .ThenBy(i => i.SeriesOrder ?? int.MaxValue)
                    .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase))
                    ItemsLayout.Children.Add(BuildItemRow(it));
                break;
        }
    }

    // High / Medium / everything-else (Low). "Low" catches any other server
    // priority string so no row is silently dropped.
    private static readonly (string Label, Func<WishlistItemSnapshot, bool> InBucket)[] PriorityBuckets =
    [
        ("High", i => i.Priority == "High"),
        ("Medium", i => i.Priority == "Medium"),
        ("Low", i => i.Priority != "High" && i.Priority != "Medium"),
    ];

    private Label GroupHeader(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 8, 0, 0),
        };
        label.SetThemeColor(Label.TextColorProperty, "BrassTextL", "BrassTextD");
        return label;
    }

    private void UpdateSortButtons()
    {
        Style(SortPriorityButton, _sort == Sort.Priority);
        Style(SortAuthorButton, _sort == Sort.Author);
        Style(SortSeriesButton, _sort == Sort.Series);

        static void Style(Button b, bool active)
        {
            if (active)
            {
                b.BackgroundColor = ThemeColors.Get("Brass");
                b.TextColor = ThemeColors.Get("InkOnBrass");
            }
            else
            {
                b.SetThemeColor(Button.BackgroundColorProperty, "SurfaceL", "SurfaceD");
                b.SetThemeColor(Button.TextColorProperty, "TextMutedL", "TextMutedD");
            }
        }
    }

    private View BuildItemRow(WishlistItemSnapshot item)
    {
        // Cover thumbnail + text stack + Bought button. Same shape as
        // ScanPage's per-Edition row (40x60 thumb, parchment placeholder).
        var coverPlaceholder = new Label
        {
            Text = "📖",
            FontSize = 18,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        coverPlaceholder.SetThemeColor(Label.TextColorProperty, "BrassTextL", "BrassTextD");
        var coverImage = new Image
        {
            IsVisible = false,
            Aspect = Aspect.AspectFit,
        };
        var coverSlot = new Grid
        {
            WidthRequest = 48,
            HeightRequest = 68,
            VerticalOptions = LayoutOptions.Start,
            Children = { coverPlaceholder, coverImage },
        };
        coverSlot.SetThemeColor(Grid.BackgroundColorProperty, "CoverMissL", "CoverMissD");

        var titleLabel = new Label
        {
            Text = item.Title,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        titleLabel.SetThemeColor(Label.TextColorProperty, "TextL", "TextD");
        var authorLabel = new Label
        {
            Text = item.Author,
            FontSize = 13,
        };
        authorLabel.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");
        var badges = new HorizontalStackLayout { Spacing = 6 };
        badges.Children.Add(PriorityBadge(item.Priority));
        if (item.SeriesId is not null && item.SeriesOrder is int seriesOrder)
        {
            // Series-order pill: chip surface + brass text (wishlist items
            // carry a plain int order — no interquel display label).
            badges.Children.Add(BuildBadge($"#{seriesOrder}", "ChipBgL", "ChipBgD", "BrassTextL", "BrassTextD"));
        }

        var textStack = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, authorLabel, badges },
        };

        var boughtButton = new Button
        {
            Text = "Bought",
            HeightRequest = 40,
            // Brass + ink-on-brass are mode-stable (the killer-accent pairing).
            BackgroundColor = ThemeColors.Get("Brass"),
            TextColor = ThemeColors.Get("InkOnBrass"),
            FontSize = 13,
            CornerRadius = 6,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 88,
        };
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 12,
            Padding = new Thickness(0, 0, 0, 4),
        };
        row.Add(coverSlot, 0, 0);
        row.Add(textStack, 1, 0);
        row.Add(boughtButton, 2, 0);

        // Wire Bought once the row exists so the handler can collapse it out.
        boughtButton.Clicked += async (_, _) => await OnBoughtAsync(item.Id, row);

        // Best-effort cover fetch — wishlist covers aren't disk-cached
        // (unlike Book covers); just stream bytes per render. If the URL
        // is missing or the fetch fails, the placeholder stays.
        _ = LoadCoverAsync(item.CoverUrl, coverImage, coverPlaceholder);

        return row;
    }

    // Pill badge resolving a light/dark token pair for both fill and text.
    private static View BuildBadge(string text, string bgLightKey, string bgDarkKey, string txLightKey, string txDarkKey)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
        };
        label.SetThemeColor(Label.TextColorProperty, txLightKey, txDarkKey);

        var border = new Border
        {
            Padding = new Thickness(6, 2),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Content = label,
        };
        border.SetThemeColor(Border.BackgroundColorProperty, bgLightKey, bgDarkKey);
        return border;
    }

    // Priority → status-tag token pair (High = miss-tag red, Medium = warn
    // amber, Low/other = owned green), each dark-mode aware.
    private static View PriorityBadge(string priority) => priority switch
    {
        "High" => BuildBadge(priority, "MissTagBgL", "MissTagBgD", "MissTagTxL", "MissTagTxD"),
        "Medium" => BuildBadge(priority, "WarnBgL", "WarnBgD", "WarnTxL", "WarnTxD"),
        _ => BuildBadge(priority, "OwnedBgL", "OwnedBgD", "OwnedTxL", "OwnedTxD"),
    };

    private async Task LoadCoverAsync(string? coverUrl, Image target, Label placeholder)
    {
        if (string.IsNullOrWhiteSpace(coverUrl)) return;
        try
        {
            var http = _httpFactory.CreateClient("covers");
            var bytes = await http.GetByteArrayAsync(coverUrl);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                target.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                target.IsVisible = true;
                placeholder.IsVisible = false;
            });
        }
        catch
        {
            // Best-effort — placeholder stays. Wishlist covers are
            // gravy, not load-bearing.
        }
    }

    private async Task OnBoughtAsync(int itemId, View row)
    {
        // Fired from an async-void click lambda — an unhandled throw here would
        // tear down the process, so a cache-write failure must surface, not crash.
        try
        {
            // Collapse the row out instead of yanking it, then reload (the rebuilt
            // list no longer carries the bought item).
            if (Motion.Enabled) await row.FadeTo(0, 160, Easing.CubicIn);
            await _cache.MarkBoughtLocallyAsync(itemId);
            await LoadFromCacheAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Couldn't mark as bought: {ex.GetType().Name}";
        }
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        Busy.IsRunning = true;
        RefreshButton.IsEnabled = false;
        StatusLabel.Text = "Refreshing wishlist…";
        try
        {
            var snapshot = await _api.GetWishlistSnapshotAsync();
            await _cache.PopulateWishlistAsync(snapshot);
            await LoadFromCacheAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Refresh failed: {ex.GetType().Name} — {ex.Message}";
        }
        finally
        {
            Busy.IsRunning = false;
            RefreshButton.IsEnabled = true;
        }
    }
}
