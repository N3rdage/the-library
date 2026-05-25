using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Services;
using BookTracker.Shared.Wishlist;
using Microsoft.Maui.Controls.Shapes;

namespace BookTracker.Mobile.Pages;

public partial class WishlistPage : ContentPage
{
    private readonly ICatalogCache _cache;
    private readonly IApiClient _api;
    private readonly IHttpClientFactory _httpFactory;

    // Palette duplicated from STYLE-GUIDE.md / MainPage.xaml so the
    // dynamic per-row Views can use Color.FromArgb. Will move to
    // {StaticResource ...} when TODO #37 (Mobile palette ResourceDictionary)
    // lands.
    private static readonly Color Leather = Color.FromArgb("#6B2737");
    private static readonly Color Brass = Color.FromArgb("#A67B3A");
    private static readonly Color AgedParchment = Color.FromArgb("#F2EADB");
    private static readonly Color Ink = Color.FromArgb("#2C2416");
    private static readonly Color FadedInk = Color.FromArgb("#6B5D4A");

    public WishlistPage(ICatalogCache cache, IApiClient api, IHttpClientFactory httpFactory)
    {
        InitializeComponent();
        _cache = cache;
        _api = api;
        _httpFactory = httpFactory;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadFromCacheAsync();
    }

    private async Task LoadFromCacheAsync()
    {
        try
        {
            var items = await _cache.GetWishlistAsync();
            RenderItems(items);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Couldn't read cached wishlist: {ex.GetType().Name}";
        }
    }

    private void RenderItems(IReadOnlyList<WishlistItemSnapshot> items)
    {
        ItemsLayout.Children.Clear();
        if (items.Count == 0)
        {
            StatusLabel.Text = "Wishlist empty. Tap Refresh wishlist to pull from the server, or add books from Bookcase web.";
            return;
        }
        StatusLabel.Text = $"{items.Count} {(items.Count == 1 ? "book" : "books")} on your wishlist.";
        foreach (var item in items)
        {
            ItemsLayout.Children.Add(BuildItemRow(item));
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
            TextColor = Brass,
        };
        var coverImage = new Image
        {
            IsVisible = false,
            Aspect = Aspect.AspectFit,
        };
        var coverSlot = new Grid
        {
            WidthRequest = 48,
            HeightRequest = 68,
            BackgroundColor = AgedParchment,
            VerticalOptions = LayoutOptions.Start,
            Children = { coverPlaceholder, coverImage },
        };

        var titleLabel = new Label
        {
            Text = item.Title,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Ink,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        var authorLabel = new Label
        {
            Text = item.Author,
            FontSize = 13,
            TextColor = FadedInk,
        };
        var badges = new HorizontalStackLayout { Spacing = 6 };
        badges.Children.Add(BuildBadge(item.Priority, PriorityColor(item.Priority)));
        if (item.SeriesId is not null && item.SeriesOrder is int seriesOrder)
        {
            badges.Children.Add(BuildBadge($"#{seriesOrder}", FadedInk));
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
            BackgroundColor = Brass,
            TextColor = Ink,
            FontSize = 13,
            CornerRadius = 6,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 88,
        };
        boughtButton.Clicked += async (_, _) => await OnBoughtAsync(item.Id);

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

        // Best-effort cover fetch — wishlist covers aren't disk-cached
        // (unlike Book covers); just stream bytes per render. If the URL
        // is missing or the fetch fails, the placeholder stays.
        _ = LoadCoverAsync(item.CoverUrl, coverImage, coverPlaceholder);

        return row;
    }

    private static View BuildBadge(string text, Color background) => new Border
    {
        BackgroundColor = background,
        Padding = new Thickness(6, 2),
        StrokeShape = new RoundRectangle { CornerRadius = 4 },
        StrokeThickness = 0,
        Content = new Label
        {
            Text = text,
            FontSize = 11,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
        },
    };

    private static Color PriorityColor(string priority) => priority switch
    {
        "High" => Color.FromArgb("#9B3B2E"),
        "Medium" => Color.FromArgb("#A67B3A"),
        "Low" => Color.FromArgb("#6B5D4A"),
        _ => Color.FromArgb("#6B5D4A"),
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

    private async Task OnBoughtAsync(int itemId)
    {
        await _cache.MarkBoughtLocallyAsync(itemId);
        await LoadFromCacheAsync();
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
