using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Theming;
using BookTracker.Shared.Catalog;

namespace BookTracker.Mobile.Pages;

// "Do I own this?" result for an ISBN, resolved against the offline cache.
// Self-contained: constructed with the ISBN, does the lookup in OnAppearing.
// Token-based + dark. v1 answers at the (this-)edition level — the work-level /
// online-edition answer is the online-layer arc (TODO #54).
public partial class ResultPage : ContentPage
{
    private readonly ICatalogCache _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _isbn;
    private bool _loaded;
    private CancellationTokenSource? _coverCts;

    public ResultPage(ICatalogCache cache, IHttpClientFactory httpFactory, string isbn)
    {
        InitializeComponent();
        _cache = cache;
        _httpFactory = httpFactory;
        _isbn = isbn;
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
            return;
        }

        StatusLabel.Text = "";
        FoundTitle.Text = book.Title;
        var formatted = ContributorFormatter.Format(book.AllAuthors);
        FoundAuthors.Text = string.IsNullOrEmpty(formatted) ? book.PrimaryAuthor : formatted;
        FoundStatusRating.Text = FormatStatusRating(book);
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

    private View BuildEditionRow(EditionSnapshot edition)
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

        var formatText = edition.EditionNumber is int n
            ? $"{FormatPrettyName(edition.Format)} · {FormatOrdinalEdition(n)}"
            : FormatPrettyName(edition.Format);
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
        row.Add(coverSlot, 0, 0);
        row.Add(textStack, 1, 0);

        // Edition thumbs use the edition CoverUrl directly (no disk cache —
        // EnsureCoverCachedAsync keys on Book.Id and would clobber the main cover).
        _ = LoadEditionCoverAsync(edition.CoverUrl, coverImage, coverPlaceholder);
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

    private static string FormatPrettyName(string format)
    {
        if (string.IsNullOrEmpty(format)) return "(unknown)";
        var sb = new System.Text.StringBuilder(format.Length + 4);
        for (int i = 0; i < format.Length; i++)
        {
            if (i > 0 && char.IsUpper(format[i]) && !char.IsUpper(format[i - 1])) sb.Append(' ');
            sb.Append(format[i]);
        }
        return sb.ToString();
    }

    private static string FormatOrdinalEdition(int n) =>
        BookTracker.Shared.Formatting.OrdinalFormatter.OrdinalEdition(n);

    private static string FormatStatusRating(BookSnapshot book)
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

    private async void OnOpenInAppClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: int bookId })
        {
            try { await Launcher.Default.OpenAsync(new Uri($"{AppConfig.ApiBaseUrl}/books/{bookId}")); }
            catch (Exception ex) { StatusLabel.Text = $"Couldn't open browser: {ex.Message}"; }
        }
    }

    private async void OnAddToLibraryClicked(object? sender, EventArgs e)
    {
        try { await Launcher.Default.OpenAsync(new Uri($"{AppConfig.ApiBaseUrl}/books/add")); }
        catch (Exception ex) { StatusLabel.Text = $"Couldn't open browser: {ex.Message}"; }
    }
}
