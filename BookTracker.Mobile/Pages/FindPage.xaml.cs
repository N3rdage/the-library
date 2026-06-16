using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Services;
using BookTracker.Mobile.Theming;
using BookTracker.Shared.Catalog;
using Microsoft.Maui.Controls.Shapes;

namespace BookTracker.Mobile.Pages;

// Unified Find tab: debounced search of the owned library (authors + works),
// an ISBN auto-detect path → ResultPage, and a scan button. Replaces MainPage
// + AuthorSearchPage + TitleSearchPage. Token-based + dark.
public partial class FindPage : ContentPage
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(250);
    private const int MinQueryLength = 2;
    private const int ResultLimit = 30;

    private readonly ICatalogCache _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ISyncService _sync;

    private CancellationTokenSource? _searchCts;
    private IReadOnlyList<AuthorSnapshot> _authors = [];
    private IReadOnlyList<BookSnapshot> _works = [];
    private string _query = "";
    private enum Scope { All, Authors, Works }
    private Scope _scope = Scope.All;

    public FindPage(ICatalogCache cache, IHttpClientFactory httpFactory, ISyncService sync)
    {
        InitializeComponent();
        _cache = cache;
        _httpFactory = httpFactory;
        _sync = sync;
        _sync.StateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshSyncChip);
        RefreshSyncChip();
        UpdateScopeButtons();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshSyncChip();
        // Focus the field — delayed because an early Focus() is a no-op before
        // the Android layout pass completes.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () => SearchEntry.Focus());
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _searchCts?.Cancel();
    }

    private void RefreshSyncChip() =>
        SyncChipLabel.Text = _sync.IsOnline ? $"⟳ {SyncService.FormatAge(_sync.Meta?.SyncedAt)}" : "⚠ offline";

    private async void OnSyncChipTapped(object? sender, TappedEventArgs e) =>
        await StatusSheetPage.OpenAsync(Navigation);

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        // Transitional: push the existing camera page. The next slice embeds an
        // inline camera here and deletes ScanPage.
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("ServiceProvider not available.");
        var page = services.GetService(typeof(ScanPage)) as ScanPage
            ?? throw new InvalidOperationException("ScanPage not registered.");
        await Navigation.PushAsync(page);
    }

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = (e.NewTextValue ?? "").Trim();
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        if (LooksLikeIsbn(query))
        {
            // ISBN path — don't search authors/works; resolve on Enter/Search.
            ResultsLayout.Children.Clear();
            ScopeSegment.IsVisible = false;
            ResultsLayout.Children.Add(Hint("Press Enter to look up this ISBN."));
            return;
        }
        if (query.Length < MinQueryLength)
        {
            ResultsLayout.Children.Clear();
            ScopeSegment.IsVisible = false;
            return;
        }

        try
        {
            await Task.Delay(DebounceWindow, ct);
            if (ct.IsCancellationRequested) return;
            var authors = await _cache.SearchAuthorsAsync(query, ResultLimit);
            if (ct.IsCancellationRequested) return;
            var works = await _cache.SearchBooksByTitleAsync(query, ResultLimit);
            if (ct.IsCancellationRequested) return;

            _authors = authors;
            _works = works;
            _query = query;
            RenderResults();
        }
        catch (TaskCanceledException) { /* typed past the debounce */ }
        catch (Exception ex)
        {
            ResultsLayout.Children.Clear();
            ScopeSegment.IsVisible = false;
            var error = new Label { Text = $"Search failed: {ex.GetType().Name} — {ex.Message}", FontSize = 14, LineBreakMode = LineBreakMode.WordWrap };
            error.SetThemeColor(Label.TextColorProperty, "MissTagTxL", "MissTagTxD");
            ResultsLayout.Children.Add(error);
        }
    }

    private async void OnSearchCompleted(object? sender, EventArgs e)
    {
        var query = (SearchEntry.Text ?? "").Trim();
        if (!LooksLikeIsbn(query)) return;
        var cleaned = new string(query.Where(c => char.IsDigit(c) || c is 'X' or 'x').ToArray());
        await Navigation.PushAsync(new ResultPage(_cache, _httpFactory, cleaned));
    }

    private void RenderResults()
    {
        ResultsLayout.Children.Clear();
        var hasAny = _authors.Count > 0 || _works.Count > 0;
        ScopeSegment.IsVisible = hasAny;

        if (!hasAny)
        {
            ResultsLayout.Children.Add(Hint($"Nothing in your library matching \"{_query}\"."));
            return;
        }

        if (_scope != Scope.Works && _authors.Count > 0)
        {
            ResultsLayout.Children.Add(GroupHeader($"Authors · {_authors.Count}"));
            foreach (var a in _authors) ResultsLayout.Children.Add(BuildAuthorCard(a));
        }
        if (_scope != Scope.Authors && _works.Count > 0)
        {
            ResultsLayout.Children.Add(GroupHeader($"Works · {_works.Count}"));
            foreach (var b in _works) ResultsLayout.Children.Add(BuildWorkCard(b));
        }
    }

    private void OnScopeAll(object? sender, EventArgs e) { _scope = Scope.All; UpdateScopeButtons(); RenderResults(); }
    private void OnScopeAuthors(object? sender, EventArgs e) { _scope = Scope.Authors; UpdateScopeButtons(); RenderResults(); }
    private void OnScopeWorks(object? sender, EventArgs e) { _scope = Scope.Works; UpdateScopeButtons(); RenderResults(); }

    private void UpdateScopeButtons()
    {
        Style(ScopeAllButton, _scope == Scope.All);
        Style(ScopeAuthorsButton, _scope == Scope.Authors);
        Style(ScopeWorksButton, _scope == Scope.Works);

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

    // ---- rows ----

    private Label GroupHeader(string text)
    {
        var label = new Label { Text = text, FontSize = 12, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 8, 0, 0) };
        label.SetThemeColor(Label.TextColorProperty, "BrassTextL", "BrassTextD");
        return label;
    }

    private Label Hint(string text)
    {
        var label = new Label { Text = text, FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap, Margin = new Thickness(0, 16, 0, 0) };
        label.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");
        return label;
    }

    private View BuildAuthorCard(AuthorSnapshot author)
    {
        var name = new Label { Text = author.Name, FontSize = 16, FontAttributes = FontAttributes.Bold };
        name.SetThemeColor(Label.TextColorProperty, "TextL", "TextD");
        var count = new Label { Text = author.BookCount == 1 ? "1 book" : $"{author.BookCount:N0} books", FontSize = 13 };
        count.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");

        var border = new Border
        {
            Style = (Style)Application.Current!.Resources["Card"],
            Padding = new Thickness(14, 12),
            Content = new VerticalStackLayout { Spacing = 4, Children = { name, count } },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Navigation.PushAsync(new AuthorWorksPage(_cache, _httpFactory, author));
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private View BuildWorkCard(BookSnapshot book)
    {
        var titleLabel = new Label { Text = book.Title, FontSize = 16, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.WordWrap };
        titleLabel.SetThemeColor(Label.TextColorProperty, "TextL", "TextD");
        var metaLabel = new Label { Text = FormatBookMeta(book), FontSize = 13, LineBreakMode = LineBreakMode.WordWrap };
        metaLabel.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");

        var coverPlaceholder = new Label { Text = "📖", FontSize = 24, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        coverPlaceholder.SetThemeColor(Label.TextColorProperty, "BrassTextL", "BrassTextD");
        var coverImage = new Image { IsVisible = false, Aspect = Aspect.AspectFit };
        var coverSlot = new Grid { WidthRequest = 56, HeightRequest = 84, VerticalOptions = LayoutOptions.Start, Children = { coverPlaceholder, coverImage } };
        coverSlot.SetThemeColor(Grid.BackgroundColorProperty, "CoverMissL", "CoverMissD");

        var layout = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 12 };
        layout.Add(coverSlot, 0, 0);
        layout.Add(new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center, Children = { titleLabel, metaLabel } }, 1, 0);

        var border = new Border
        {
            Style = (Style)Application.Current!.Resources["Card"],
            Padding = new Thickness(12, 10),
            Content = layout,
        };
        // Search hits are owned books — tap deep-links to the web detail.
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenInAppAsync(book.Id);
        border.GestureRecognizers.Add(tap);

        _ = Covers.LoadIntoAsync(_cache, _httpFactory, book.Id, coverImage, coverPlaceholder);
        return border;
    }

    private static string FormatBookMeta(BookSnapshot book)
    {
        var parts = new List<string>();
        if (book.Rating > 0)
        {
            var clamped = Math.Clamp(book.Rating, 0, 5);
            parts.Add(new string('★', clamped) + new string('☆', 5 - clamped));
        }
        if (!string.IsNullOrWhiteSpace(book.Status)) parts.Add(book.Status);
        return parts.Count == 0 ? "" : string.Join("  ·  ", parts);
    }

    private async Task OpenInAppAsync(int bookId)
    {
        try { await Launcher.Default.OpenAsync(new Uri($"{AppConfig.ApiBaseUrl}/books/{bookId}")); }
        catch { /* best-effort */ }
    }

    // 10–13 chars, digits with an optional trailing X (ISBN-10 check digit).
    private static bool LooksLikeIsbn(string q)
    {
        if (q.Length is < 10 or > 13) return false;
        for (int i = 0; i < q.Length; i++)
        {
            var c = q[i];
            if (char.IsDigit(c)) continue;
            if ((c is 'X' or 'x') && i == q.Length - 1) continue;
            return false;
        }
        return true;
    }
}
