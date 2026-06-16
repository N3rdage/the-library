using BookTracker.Mobile.Cache;
using BookTracker.Shared.Catalog;
using Microsoft.Maui.Controls.Shapes;

namespace BookTracker.Mobile.Pages;

public partial class AuthorSearchPage : ContentPage
{
    private readonly ICatalogCache _cache;

    // Debounce parameters mirror the Web /bookshop author lookup
    // (BookTracker.Web/wwwroot/js/catalog-cache.js): 250ms after the
    // last keystroke, minimum 2 characters. Cancellation ensures
    // stale searches don't render after the user has typed past them.
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(250);
    private const int MinQueryLength = 2;
    private const int ResultLimit = 30;

    // Palette references — duplicated literals from STYLE-GUIDE.md so
    // code that creates Views dynamically can use Color.FromArgb. When
    // TODO #37 (Mobile palette ResourceDictionary) lands these move to
    // {StaticResource Leather} etc.
    private static readonly Color Brass = Color.FromArgb("#A67B3A");
    private static readonly Color AgedParchment = Color.FromArgb("#F2EADB");
    private static readonly Color Ink = Color.FromArgb("#2C2416");
    private static readonly Color FadedInk = Color.FromArgb("#6B5D4A");
    private static readonly Color ErrorColor = Color.FromArgb("#9B3B2E");

    private CancellationTokenSource? _searchCts;
    private readonly IHttpClientFactory _httpFactory;

    public AuthorSearchPage(ICatalogCache cache, IHttpClientFactory httpFactory)
    {
        InitializeComponent();
        _cache = cache;
        _httpFactory = httpFactory;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Auto-focus the search field. Brief delay because Focus()
        // called too early in the page lifecycle is a no-op on some
        // Android versions (the layout pass hasn't completed yet).
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
        {
            SearchEntry.Focus();
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _searchCts?.Cancel();
    }

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = (e.NewTextValue ?? "").Trim();

        // Cancel any in-flight search so a fast typist doesn't see
        // stale results overwrite their newer query.
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        if (query.Length < MinQueryLength)
        {
            ClearResults();
            return;
        }

        try
        {
            await Task.Delay(DebounceWindow, ct);
            if (ct.IsCancellationRequested) return;

            var results = await _cache.SearchAuthorsAsync(query, ResultLimit);
            if (ct.IsCancellationRequested) return;

            RenderResults(results, query);
        }
        catch (TaskCanceledException)
        {
            // Expected — user typed past the debounce.
        }
        catch (Exception ex)
        {
            ResultsLayout.Children.Clear();
            HintLabel.IsVisible = false;
            ResultsLayout.Children.Add(new Label
            {
                Text = $"Search failed: {ex.GetType().Name} — {ex.Message}",
                TextColor = ErrorColor,
                FontSize = 14,
                LineBreakMode = LineBreakMode.WordWrap,
            });
        }
    }

    private void ClearResults()
    {
        ResultsLayout.Children.Clear();
        HintLabel.IsVisible = true;
    }

    private void RenderResults(IReadOnlyList<AuthorSnapshot> results, string query)
    {
        ResultsLayout.Children.Clear();
        HintLabel.IsVisible = false;

        if (results.Count == 0)
        {
            ResultsLayout.Children.Add(new Label
            {
                Text = $"No authors found matching \"{query}\".",
                FontSize = 14,
                TextColor = FadedInk,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 16, 0, 0),
            });
            return;
        }

        foreach (var author in results)
        {
            ResultsLayout.Children.Add(BuildAuthorCard(author));
        }
    }

    private View BuildAuthorCard(AuthorSnapshot author)
    {
        var name = new Label
        {
            Text = author.Name,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Ink,
        };
        var count = new Label
        {
            // Pluralise the "book" suffix — '1 book' reads better than
            // '1 books'. Anything else uses the N,000 group format.
            Text = author.BookCount == 1
                ? "1 book"
                : $"{author.BookCount:N0} books",
            FontSize = 13,
            TextColor = FadedInk,
        };

        var border = new Border
        {
            Stroke = Brass,
            StrokeThickness = 1,
            BackgroundColor = AgedParchment,
            Padding = new Thickness(14, 12),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children = { name, count },
            },
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenAuthorBooksAsync(author);
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private async Task OpenAuthorBooksAsync(AuthorSnapshot author)
    {
        // AuthorWorksPage takes ICatalogCache + the cover http factory via DI
        // plus the runtime-chosen author. Constructed inline because the
        // AuthorSnapshot isn't something DI can supply.
        var page = new AuthorWorksPage(_cache, _httpFactory, author);
        await Navigation.PushAsync(page);
    }
}
