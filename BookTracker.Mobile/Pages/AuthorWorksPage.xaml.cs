using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Cache.Presentation;
using BookTracker.Mobile.Services;
using BookTracker.Mobile.Theming;
using BookTracker.Shared.Catalog;
using Microsoft.Maui.Networking;

namespace BookTracker.Mobile.Pages;

// The works by an author. Token-based + dark; replaces the old AuthorBooksPage.
// The owned works render first from the cache. When the phone is online,
// Bookcase's GET /api/online/author-works is then asked for the author's
// bibliography; AuthorWorksMerger adds the works the library does not own
// (muted, "Not owned") and the All · Missing only segment appears. Offline,
// on failure, or while the call is in flight the owned list stands alone.
// Reached from the search flow with a runtime-chosen AuthorSnapshot.
public partial class AuthorWorksPage : ContentPage
{
    private readonly ICatalogCache _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IApiClient _api;
    private readonly AuthorSnapshot _author;
    private bool _loaded;
    private bool _missingOnly;
    private CancellationTokenSource? _onlineCts;

    // Owned cards by Book id, built once from the cache and reused when the
    // online list arrives so their covers are not loaded twice.
    private readonly Dictionary<int, View> _ownedCards = [];
    private IReadOnlyList<BookSnapshot> _owned = [];
    private IReadOnlyList<AuthorWorkRow> _rows = [];
    private readonly List<(AuthorWorkRow Row, View Card)> _rowCards = [];
    private Label? _nothingMissingHint;

    public AuthorWorksPage(ICatalogCache cache, IHttpClientFactory httpFactory, IApiClient api, AuthorSnapshot author)
    {
        InitializeComponent();
        _cache = cache;
        _httpFactory = httpFactory;
        _api = api;
        _author = author;
        Title = author.Name;
        HeaderLabel.Text = author.BookCount == 1
            ? "1 book in your library"
            : $"{author.BookCount:N0} books in your library";
        UpdateScopeButtons();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return; // pushed page, fresh instance per visit — load once
        _loaded = true;
        _ = ((VisualElement)Content).InAsync(); // soften the push
        await LoadWorksAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Popping mid-call: cancel the online fetch so its UI-thread
        // continuation doesn't mutate this (now off-stack) page.
        _onlineCts?.Cancel();
    }

    private async Task LoadWorksAsync()
    {
        WorksLayout.Children.Clear();
        var spinner = new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Center };
        spinner.SetThemeColor(ActivityIndicator.ColorProperty, "LeatherL", "LeatherD");
        WorksLayout.Children.Add(spinner);

        IReadOnlyList<BookSnapshot> books;
        try
        {
            books = await _cache.LookupByAuthorAsync(_author.CanonicalId);
        }
        catch (Exception ex)
        {
            WorksLayout.Children.Clear();
            var error = new Label
            {
                Text = $"Couldn't load books: {ex.GetType().Name} — {ex.Message}",
                FontSize = 14,
                LineBreakMode = LineBreakMode.WordWrap,
            };
            error.SetThemeColor(Label.TextColorProperty, "MissTagTxL", "MissTagTxD");
            WorksLayout.Children.Add(error);
            return;
        }

        _owned = books;
        WorksLayout.Children.Clear();

        if (books.Count == 0)
        {
            WorksLayout.Children.Add(Hint("No books in your library for this author."));
        }
        else
        {
            foreach (var book in books)
            {
                var card = BuildBookCard(book);
                _ownedCards[book.Id] = card;
                WorksLayout.Children.Add(card);
            }
        }

        // The owned list is already on screen; the online list only ever
        // adds to it, never delays it.
        if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
        {
            _onlineCts = new CancellationTokenSource();
            _ = LoadOnlineAsync(_onlineCts.Token);
        }
    }

    private async Task LoadOnlineAsync(CancellationToken ct)
    {
        var online = await _api.GetAuthorWorksOnlineAsync(_author.Name, ct); // null on any failure
        if (online is null || online.Works.Count == 0 || ct.IsCancellationRequested) return;

        var rows = AuthorWorksMerger.Merge(_owned, online.Works);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (ct.IsCancellationRequested) return;

            _rows = rows;
            _rowCards.Clear();
            WorksLayout.Children.Clear();
            foreach (var row in _rows)
            {
                View card = row.OwnedBook is { } book && _ownedCards.TryGetValue(book.Id, out var built)
                    ? built
                    : BuildOnlineCard(row);
                _rowCards.Add((row, card));
                WorksLayout.Children.Add(card);
            }

            _nothingMissingHint = Hint(AuthorWorksMerger.NothingMissingHint);
            WorksLayout.Children.Add(_nothingMissingHint);

            HeaderLabel.Text = AuthorWorksMerger.Summary(_rows);
            ApplyScope();
            ScopeSegment.IsVisible = true;
            _ = WorksLayout.InAsync(rise: 8);
        });
    }

    // ---- scope ----

    private void OnScopeAll(object? sender, EventArgs e) { _missingOnly = false; UpdateScopeButtons(); ApplyScope(); }
    private void OnScopeMissing(object? sender, EventArgs e) { _missingOnly = true; UpdateScopeButtons(); ApplyScope(); }

    // Shows the cards AuthorWorksMerger.Filter keeps; the cards are built once.
    private void ApplyScope()
    {
        var visible = new HashSet<AuthorWorkRow>(
            AuthorWorksMerger.Filter(_rows, _missingOnly), ReferenceEqualityComparer.Instance);
        foreach (var (row, card) in _rowCards)
            card.IsVisible = visible.Contains(row);
        if (_nothingMissingHint is not null)
            _nothingMissingHint.IsVisible = _missingOnly && visible.Count == 0;
    }

    private void UpdateScopeButtons()
    {
        Style(ScopeAllButton, !_missingOnly);
        Style(ScopeMissingButton, _missingOnly);

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

    private static Label Hint(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(0, 16, 0, 0),
        };
        label.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");
        return label;
    }

    private View BuildBookCard(BookSnapshot book)
    {
        var (coverSlot, coverImage, coverPlaceholder) = BuildCoverSlot();
        var border = BuildCard(book.Title, BookMetaFormatter.FormatBookMeta(book), coverSlot);

        // Tap → deep-link to the web book detail (mobile stays read-only in v1).
        AddOpenTap(border, book.Id);

        _ = Covers.LoadIntoAsync(_cache, _httpFactory, book.Id, coverImage, coverPlaceholder);
        return border;
    }

    // A row from the online list: a work the library does not own (muted, no
    // action), or one Bookcase reports as owned that the cache does not hold
    // yet (opens the web book detail like any owned row).
    private View BuildOnlineCard(AuthorWorkRow row)
    {
        var (coverSlot, _, _) = BuildCoverSlot();
        if (row.OwnedBookId is int bookId)
        {
            var owned = BuildCard(row.Title, AuthorWorksMerger.NotSyncedMeta, coverSlot);
            AddOpenTap(owned, bookId);
            return owned;
        }

        var missing = BuildCard(row.Title, AuthorWorksMerger.MissingMeta(row), coverSlot);
        missing.Opacity = 0.8;
        return missing;
    }

    private static (Grid Slot, Image Image, Label Placeholder) BuildCoverSlot()
    {
        var coverPlaceholder = new Label
        {
            Text = "📖",
            FontSize = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        coverPlaceholder.SetThemeColor(Label.TextColorProperty, "BrassTextL", "BrassTextD");
        var coverImage = new Image { IsVisible = false, Aspect = Aspect.AspectFit };
        var coverSlot = new Grid
        {
            WidthRequest = 56,
            HeightRequest = 84,
            VerticalOptions = LayoutOptions.Start,
            Children = { coverPlaceholder, coverImage },
        };
        coverSlot.SetThemeColor(Grid.BackgroundColorProperty, "CoverMissL", "CoverMissD");
        return (coverSlot, coverImage, coverPlaceholder);
    }

    private static Border BuildCard(string title, string meta, View coverSlot)
    {
        var titleLabel = new Label
        {
            Text = title,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        titleLabel.SetThemeColor(Label.TextColorProperty, "TextL", "TextD");

        var metaLabel = new Label
        {
            Text = meta,
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        metaLabel.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");

        var textStack = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, metaLabel },
        };

        var layout = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12,
        };
        layout.Add(coverSlot, 0, 0);
        layout.Add(textStack, 1, 0);

        return new Border
        {
            Style = (Style)Application.Current!.Resources["Card"],
            Padding = new Thickness(12, 10),
            Content = layout,
        };
    }

    private void AddOpenTap(Border border, int bookId)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenInAppAsync(bookId);
        border.GestureRecognizers.Add(tap);
    }

    private async Task OpenInAppAsync(int bookId)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri($"{AppConfig.ApiBaseUrl}/books/{bookId}"));
        }
        catch (Exception ex)
        {
            var toast = new Label
            {
                Text = $"Couldn't open browser: {ex.Message}",
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8),
            };
            toast.SetThemeColor(Label.TextColorProperty, "MissTagTxL", "MissTagTxD");
            WorksLayout.Children.Insert(0, toast);
        }
    }
}
