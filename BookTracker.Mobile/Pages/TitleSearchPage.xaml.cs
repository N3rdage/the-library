using BookTracker.Mobile.Cache;
using BookTracker.Shared.Catalog;
using Microsoft.Maui.Controls.Shapes;

namespace BookTracker.Mobile.Pages;

public partial class TitleSearchPage : ContentPage
{
    private readonly ICatalogCache _cache;
    private readonly IHttpClientFactory _httpFactory;

    // Same debounce parameters as AuthorSearchPage — 250ms after the
    // last keystroke, min 2 characters. Keeps the typing-to-results
    // feel consistent between the two search modes.
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(250);
    private const int MinQueryLength = 2;
    private const int ResultLimit = 30;

    // Palette references — duplicated literals per the same convention
    // as AuthorSearchPage. When TODO #37 (Mobile palette ResourceDictionary)
    // lands these consolidate into {StaticResource ...}.
    private static readonly Color Brass = Color.FromArgb("#A67B3A");
    private static readonly Color AgedParchment = Color.FromArgb("#F2EADB");
    private static readonly Color CoverPlaceholderBg = Color.FromArgb("#E0DAC8");
    private static readonly Color Ink = Color.FromArgb("#2C2416");
    private static readonly Color FadedInk = Color.FromArgb("#6B5D4A");
    private static readonly Color ErrorColor = Color.FromArgb("#9B3B2E");

    private CancellationTokenSource? _searchCts;

    public TitleSearchPage(ICatalogCache cache, IHttpClientFactory httpFactory)
    {
        InitializeComponent();
        _cache = cache;
        _httpFactory = httpFactory;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
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

            var results = await _cache.SearchBooksByTitleAsync(query, ResultLimit);
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

    private void RenderResults(IReadOnlyList<BookSnapshot> results, string query)
    {
        ResultsLayout.Children.Clear();
        HintLabel.IsVisible = false;

        if (results.Count == 0)
        {
            ResultsLayout.Children.Add(new Label
            {
                Text = $"No books found matching \"{query}\".",
                FontSize = 14,
                TextColor = FadedInk,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 16, 0, 0),
            });
            return;
        }

        foreach (var book in results)
        {
            ResultsLayout.Children.Add(BuildBookCard(book));
        }
    }

    // Same card shape as AuthorBooksPage.BuildBookCard so titles search
    // and author drill-down look identical. Cover thumb (left) + text
    // stack (title / author / status badge). Intentional duplication
    // for v1 — extract to a shared helper when a third use site appears.
    private View BuildBookCard(BookSnapshot book)
    {
        var titleLabel = new Label
        {
            Text = book.Title,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Ink,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        var authorLabel = new Label
        {
            Text = book.PrimaryAuthor,
            FontSize = 13,
            TextColor = FadedInk,
        };
        var metaLabel = new Label
        {
            Text = FormatBookMeta(book),
            FontSize = 13,
            TextColor = FadedInk,
            LineBreakMode = LineBreakMode.WordWrap,
            IsVisible = !string.IsNullOrEmpty(FormatBookMeta(book)),
        };

        var coverPlaceholder = new Label
        {
            Text = "📖",
            FontSize = 24,
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
            WidthRequest = 56,
            HeightRequest = 84,
            BackgroundColor = CoverPlaceholderBg,
            VerticalOptions = LayoutOptions.Start,
            Children = { coverPlaceholder, coverImage },
        };

        var textStack = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, authorLabel, metaLabel },
        };

        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 12,
        };
        layout.Add(coverSlot, 0, 0);
        layout.Add(textStack, 1, 0);

        var border = new Border
        {
            Stroke = Brass,
            StrokeThickness = 1,
            BackgroundColor = AgedParchment,
            Padding = new Thickness(12, 10),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = layout,
        };

        // No tap gesture — v1 keeps title search as a confirm-I-have-it
        // affordance. The status badge below the author answers the
        // follow-up "have I read it" question. If we add tap-through
        // later (e.g. open per-book detail), wire a TapGestureRecognizer
        // here matching AuthorBooksPage.BuildBookCard.

        _ = LoadCoverAsync(book.Id, coverImage, coverPlaceholder);

        return border;
    }

    private async Task LoadCoverAsync(int bookId, Image target, Label placeholder)
    {
        try
        {
            var http = _httpFactory.CreateClient("covers");
            var path = await _cache.EnsureCoverCachedAsync(bookId, http);
            if (path is null) return;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                target.Source = ImageSource.FromFile(path);
                target.IsVisible = true;
                placeholder.IsVisible = false;
            });
        }
        catch
        {
            // Placeholder stays. Next render of the row (page revisit /
            // re-search) will retry via EnsureCoverCachedAsync.
        }
    }

    private static string FormatBookMeta(BookSnapshot book)
    {
        // "★★★☆☆ · Read" — rating stars + status. Matches the format
        // AuthorBooksPage uses so the row reads consistently across
        // search modes.
        var parts = new List<string>();
        if (book.Rating > 0)
        {
            var clamped = Math.Clamp(book.Rating, 0, 5);
            parts.Add(new string('★', clamped) + new string('☆', 5 - clamped));
        }
        if (!string.IsNullOrWhiteSpace(book.Status))
        {
            parts.Add(book.Status);
        }
        return parts.Count == 0 ? "" : string.Join("  ·  ", parts);
    }
}
