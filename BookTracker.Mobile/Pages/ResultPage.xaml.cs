using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Cache.Presentation;
using BookTracker.Mobile.Services;
using BookTracker.Mobile.Theming;
using BookTracker.Shared.Catalog;
using Microsoft.Maui.Networking;

namespace BookTracker.Mobile.Pages;

// "Do I own this?" result for an ISBN, resolved against the offline cache.
// Self-contained: constructed with the ISBN, does the lookup in OnAppearing.
// Token-based + dark. A cache hit answers at the (this-)edition level. On a
// miss the offline "not found" answer renders at once; when the phone is
// online, Bookcase's GET /api/online/isbn/{isbn} is then asked and its
// work-level answer (OnlineResultFormatter) replaces the "not found" card.
public partial class ResultPage : ContentPage
{
    private readonly ICatalogCache _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IApiClient _api;
    private readonly string _isbn;
    private bool _loaded;
    private CancellationTokenSource? _coverCts;

    public ResultPage(ICatalogCache cache, IHttpClientFactory httpFactory, IApiClient api, string isbn)
    {
        InitializeComponent();
        _cache = cache;
        _httpFactory = httpFactory;
        _api = api;
        _isbn = isbn;
        OnlineCoverPlaceholder.Text = TablerIcons.Book;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return; // pushed page, fresh per visit
        _loaded = true;
        await LookupAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Popping mid-load: cancel the enriched-detail fetch so its UI-thread
        // continuation doesn't mutate this (now off-stack) page.
        _coverCts?.Cancel();
    }

    private async Task LookupAsync()
    {
        StatusLabel.Text = $"Looking up {_isbn}…";

        BookSnapshot? book;
        try
        {
            book = await _cache.LookupByIsbnAsync(_isbn);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Lookup failed: {ex.GetType().Name} — {ex.Message}";
            return;
        }

        // Wishlist flag — independent of ownership. Best-effort.
        try { WishlistFlag.IsVisible = await _cache.IsWishlistedIsbnAsync(_isbn); }
        catch { /* gravy */ }

        if (book is null)
        {
            StatusLabel.Text = "";
            MissingIsbn.Text = $"ISBN: {_isbn}";
            MissingFrame.IsVisible = true;
            _ = MissingFrame.InAsync(); // answer + motion land together

            // The offline answer is already on screen; the online one only
            // ever replaces it, never delays it.
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                _coverCts = new CancellationTokenSource();
                _ = LookupOnlineAsync(_coverCts.Token);
            }
            return;
        }

        StatusLabel.Text = "";
        FoundTitle.Text = book.Title;
        var formatted = ContributorFormatter.Format(book.AllAuthors);
        FoundAuthors.Text = string.IsNullOrEmpty(formatted) ? book.PrimaryAuthor : formatted;
        FoundStatusRating.Text = BookMetaFormatter.FormatBookMeta(book);
        FoundIsbn.Text = $"ISBN: {_isbn}";
        OpenInAppButton.CommandParameter = book.Id;
        FoundFrame.IsVisible = true;
        _ = FoundFrame.InAsync(); // answer + motion land together

        _coverCts = new CancellationTokenSource();
        _ = Covers.LoadIntoAsync(_cache, _httpFactory, book.Id, FoundCover, FoundCoverPlaceholder);
        _ = LoadEnrichedDetailAsync(book.Id, _coverCts.Token);
    }

    private async Task LoadEnrichedDetailAsync(int bookId, CancellationToken ct)
    {
        BookEnrichedDetail? detail;
        try { detail = await _cache.GetBookEnrichedDetailAsync(bookId); }
        catch { return; } // enriched detail is gravy
        if (detail is null || ct.IsCancellationRequested) return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (ct.IsCancellationRequested) return;

            // Works: only meaningful for multi-Work compendiums.
            if (detail.Works.Count > 1)
            {
                foreach (var work in detail.Works)
                    WorksLayout.Children.Add(BuildWorkRow(work));
                WorksSection.IsVisible = true;
            }

            // Editions: only when you own more than one.
            if (detail.Editions.Count > 1)
            {
                foreach (var edition in detail.Editions)
                    EditionsLayout.Children.Add(BuildEditionRow(edition));
                EditionsSection.IsVisible = true;
            }
        });
    }

    private async Task LookupOnlineAsync(CancellationToken ct)
    {
        OnlineCheckingLabel.IsVisible = true;
        var lookup = await _api.LookupIsbnOnlineAsync(_isbn, ct); // null on any failure
        var answer = OnlineResultFormatter.Decide(lookup, wasOnline: true);

        BookEnrichedDetail? owned = null;
        if (answer.OwnedBookId is int bookId && !ct.IsCancellationRequested)
        {
            try { owned = await _cache.GetBookEnrichedDetailAsync(bookId); }
            catch { /* the owned editions are gravy; the answer still renders */ }
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (ct.IsCancellationRequested) return;
            OnlineCheckingLabel.IsVisible = false;
            if (answer.State == OnlineResultState.Unknown) return; // "not found" stays

            OnlineHeadline.Text = answer.Headline;
            SetLine(OnlineTitle, answer.Title);
            SetLine(OnlineByline, answer.Byline);
            SetLine(OnlineDetails, answer.Details);
            OnlineIsbn.Text = $"ISBN: {_isbn}";
            SetLine(OnlineNote, answer.Note);

            if (owned is not null && owned.Editions.Count > 0)
            {
                foreach (var edition in owned.Editions)
                    OnlineEditionsLayout.Children.Add(BuildEditionRow(edition));
                OnlineEditionsSection.IsVisible = true;
            }

            OnlineOpenInAppButton.IsVisible = answer.OwnedBookId is not null;
            OnlineOpenInAppButton.CommandParameter = answer.OwnedBookId;
            OnlineSyncButton.IsVisible = answer.State == OnlineResultState.OwnedSameEdition;
            OnlineAddButton.IsVisible = answer.State == OnlineResultState.NotOwned;

            OtherEditionsTitle.Text = WorkEditionsOrdering.RowTitle;
            OtherEditionsCaption.Text = WorkEditionsOrdering.OnlineOnlyCaption;
            OtherEditionsSection.IsVisible = true;

            MissingFrame.IsVisible = false;
            OnlineFrame.IsVisible = true;
            _ = OnlineFrame.InAsync();
        });

        if (lookup is not null && !ct.IsCancellationRequested && answer.State != OnlineResultState.Unknown)
            _ = LoadEditionCoverAsync(lookup.CoverUrl, OnlineCover, OnlineCoverPlaceholder);

        if (lookup?.WorkKey is { Length: > 0 } workKey && !ct.IsCancellationRequested
            && answer.State != OnlineResultState.Unknown)
            await LoadOtherEditionsAsync(workKey, owned, ct);
    }

    // The row is already on screen disabled; it only ever lights up, and a
    // failed or empty fetch leaves it as it was.
    private async Task LoadOtherEditionsAsync(string workKey, BookEnrichedDetail? owned, CancellationToken ct)
    {
        var editions = await _api.GetWorkEditionsOnlineAsync(workKey, ct); // null on any failure
        if (editions is null || ct.IsCancellationRequested) return;

        var rows = WorkEditionsOrdering.Arrange(editions.Editions, owned?.Editions.Select(e => e.Isbn) ?? []);
        if (rows.Count == 0) return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (ct.IsCancellationRequested) return;

            foreach (var row in rows)
                OtherEditionsLayout.Children.Add(BuildOtherEditionRow(row));

            OtherEditionsTitle.SetThemeColor(Label.TextColorProperty, "TextL", "TextD");
            OtherEditionsCaption.Text = rows.Count == 1 ? "1 edition" : $"{rows.Count:N0} editions";
            OtherEditionsChevron.Text = "▸";
            OtherEditionsChevron.IsVisible = true;
        });
    }

    private void OnOtherEditionsTapped(object? sender, TappedEventArgs e)
    {
        if (!OtherEditionsChevron.IsVisible) return; // disabled: nothing to expand
        OtherEditionsLayout.IsVisible = !OtherEditionsLayout.IsVisible;
        OtherEditionsChevron.Text = OtherEditionsLayout.IsVisible ? "▾" : "▸";
    }

    private static void SetLine(Label label, string text)
    {
        label.Text = text;
        label.IsVisible = text.Length > 0;
    }

    private static View BuildWorkRow(WorkSnapshot work)
    {
        var byline = work.Contributors is { Count: > 0 }
            ? ContributorFormatter.Format(work.Contributors)
            : null;
        if (string.IsNullOrEmpty(byline)) byline = work.PrimaryAuthor;

        var label = new Label
        {
            Text = string.IsNullOrWhiteSpace(byline) ? work.Title : $"{work.Title} — {byline}",
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        label.SetThemeColor(Label.TextColorProperty, "TextL", "TextD");
        return label;
    }

    // A 40x60 thumbnail slot. Edition thumbs use the edition CoverUrl directly
    // (no disk cache — EnsureCoverCachedAsync keys on Book.Id and would
    // clobber the main cover).
    private Grid BuildEditionCoverSlot(string? coverUrl)
    {
        var coverPlaceholder = new Label
        {
            Text = "📖",
            FontSize = 16,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        coverPlaceholder.SetThemeColor(Label.TextColorProperty, "BrassTextL", "BrassTextD");
        var coverImage = new Image { IsVisible = false, Aspect = Aspect.AspectFit };
        var coverSlot = new Grid
        {
            WidthRequest = 40,
            HeightRequest = 60,
            VerticalOptions = LayoutOptions.Start,
            Children = { coverPlaceholder, coverImage },
        };
        coverSlot.SetThemeColor(Grid.BackgroundColorProperty, "CoverMissL", "CoverMissD");
        _ = LoadEditionCoverAsync(coverUrl, coverImage, coverPlaceholder);
        return coverSlot;
    }

    private View BuildOtherEditionRow(WorkEditionRow edition)
    {
        var heading = new Label
        {
            Text = string.IsNullOrWhiteSpace(edition.Title) ? "Untitled edition" : edition.Title,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        heading.SetThemeColor(Label.TextColorProperty, "TextL", "TextD");
        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { heading },
        };

        var meta = WorkEditionsOrdering.Meta(edition);
        if (meta.Length > 0)
        {
            var metaLabel = new Label { Text = meta, FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
            metaLabel.SetThemeColor(Label.TextColorProperty, "TextL", "TextD");
            textStack.Children.Add(metaLabel);
        }

        var isbnText = string.IsNullOrWhiteSpace(edition.Isbn) ? "no ISBN" : edition.Isbn;
        var isbnLabel = new Label
        {
            Text = edition.IsOwned ? $"{isbnText}  ·  ✓ You own this" : isbnText,
            FontSize = 11,
            FontFamily = "Courier New",
        };
        isbnLabel.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");
        textStack.Children.Add(isbnLabel);

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10,
        };
        row.Add(BuildEditionCoverSlot(edition.CoverUrl), 0, 0);
        row.Add(textStack, 1, 0);
        return row;
    }

    private View BuildEditionRow(EditionSnapshot edition)
    {
        var formatText = edition.EditionNumber is int n
            ? $"{EditionFormatName.FormatPrettyName(edition.Format)} · {FormatOrdinalEdition(n)}"
            : EditionFormatName.FormatPrettyName(edition.Format);
        var formatLabel = new Label { Text = formatText, FontSize = 13, FontAttributes = FontAttributes.Bold };
        formatLabel.SetThemeColor(Label.TextColorProperty, "TextL", "TextD");
        var isbnLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(edition.Isbn) ? "no ISBN" : edition.Isbn,
            FontSize = 11,
            FontFamily = "Courier New",
        };
        isbnLabel.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");

        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { formatLabel, isbnLabel },
        };

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10,
        };
        row.Add(BuildEditionCoverSlot(edition.CoverUrl), 0, 0);
        row.Add(textStack, 1, 0);
        return row;
    }

    private async Task LoadEditionCoverAsync(string? coverUrl, Image target, Label placeholder)
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
        catch { /* placeholder stays */ }
    }

    private static string FormatOrdinalEdition(int n) =>
        BookTracker.Shared.Formatting.OrdinalFormatter.OrdinalEdition(n);

    private async void OnOpenInAppClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: int bookId })
        {
            try { await Launcher.Default.OpenAsync(new Uri($"{AppConfig.ApiBaseUrl}/books/{bookId}")); }
            catch (Exception ex) { StatusLabel.Text = $"Couldn't open browser: {ex.Message}"; }
        }
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        try { await StatusSheetPage.OpenAsync(Navigation); }
        catch (Exception ex) { StatusLabel.Text = $"Couldn't open sync: {ex.Message}"; }
    }

    private async void OnAddToLibraryClicked(object? sender, EventArgs e)
    {
        try { await Launcher.Default.OpenAsync(new Uri($"{AppConfig.ApiBaseUrl}/books/add")); }
        catch (Exception ex) { StatusLabel.Text = $"Couldn't open browser: {ex.Message}"; }
    }
}
