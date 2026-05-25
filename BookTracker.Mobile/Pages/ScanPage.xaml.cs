using BookTracker.Mobile.Cache;
using BookTracker.Shared.Catalog;
using ZXing.Net.Maui;

namespace BookTracker.Mobile.Pages;

public partial class ScanPage : ContentPage
{
    private readonly ICatalogCache _cache;
    private readonly IHttpClientFactory _httpFactory;

    // Debounce — barcode scanners fire detect events ~10x/sec while
    // the code is in frame. We only want one lookup per "real" scan,
    // and re-pointing at the same book shouldn't immediately re-fire.
    // Mirrors the 3s window from BookTracker.Web/wwwroot/js/barcode-scanner.js.
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(3);
    private string? _lastScannedIsbn;
    private DateTime _lastScannedAt = DateTime.MinValue;

    // Cancels any in-flight cover fetch when a new lookup starts —
    // otherwise scanning two books in a row could land Book A's cover
    // bytes onto the result card for Book B.
    private CancellationTokenSource? _coverCts;

    public ScanPage(ICatalogCache cache, IHttpClientFactory httpFactory)
    {
        InitializeComponent();
        _cache = cache;
        _httpFactory = httpFactory;

        // Filter the camera reader to book barcodes only — EAN-13 is
        // the ~99% case (modern ISBN-13); EAN-8 catches the rare
        // short-form prints. Multi-format scanning slows decode +
        // false-positives off other patterns.
        Reader.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormat.Ean13 | BarcodeFormat.Ean8,
            AutoRotate = true,
            Multiple = false,
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Runtime CAMERA permission request. On Android 6+ the
        // manifest declaration alone isn't enough — we have to
        // explicitly ask. Permissions.Camera handles both initial
        // request + "go to settings" if the user permanently denied
        // earlier. We don't try to recover gracefully in v1 — if
        // they deny, the camera view stays black and manual entry
        // remains usable.
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Camera>();
        }
        if (status != PermissionStatus.Granted)
        {
            StatusLabel.Text = "Camera permission denied. Manual ISBN entry still works.";
            Reader.IsDetecting = false;
        }
        else
        {
            Reader.IsDetecting = true;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Stop the camera feed when leaving the page — the
        // CameraBarcodeReaderView keeps the camera open while
        // IsDetecting is true.
        Reader.IsDetecting = false;
    }

    private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        // BarcodesDetected fires on a background thread; UI updates
        // must marshal back to main. ZXing also re-fires while the
        // code stays in frame, hence the debounce.
        var result = e.Results.FirstOrDefault();
        if (result is null) return;

        var isbn = (result.Value ?? "").Trim();
        if (string.IsNullOrEmpty(isbn)) return;

        var now = DateTime.UtcNow;
        if (isbn == _lastScannedIsbn && (now - _lastScannedAt) < DebounceWindow)
        {
            return;
        }
        _lastScannedIsbn = isbn;
        _lastScannedAt = now;

        await MainThread.InvokeOnMainThreadAsync(() => LookupAsync(isbn));
    }

    private void OnAppendXClicked(object? sender, EventArgs e)
    {
        // Append the ISBN-10 check digit. Numeric keyboard stays as
        // default so the 99% case (typing 10-13 digits) keeps its big
        // keys; this button is the alternate path for the ~1% case
        // where the check digit is X. OnManualLookupClicked already
        // permits X / x in its cleanup pass, so no validation change.
        var current = ManualEntry.Text ?? "";
        ManualEntry.Text = current + "X";
        // Re-focus so the keyboard stays up — users typically follow
        // up immediately (tap Lookup, or backspace the X if it was a
        // misfire). Cursor goes to the end so further typing appends.
        ManualEntry.Focus();
        ManualEntry.CursorPosition = ManualEntry.Text.Length;
    }

    private async void OnManualLookupClicked(object? sender, EventArgs e)
    {
        var raw = (ManualEntry.Text ?? "").Trim();
        // Strip non-alphanumeric (allow trailing X for ISBN-10 check
        // digit) so a hyphenated barcode pasted from a website still
        // resolves. The cache stores ISBNs unhyphenated.
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').ToArray());
        if (cleaned.Length < 10 || cleaned.Length > 13)
        {
            StatusLabel.Text = "Enter a 10- or 13-digit ISBN.";
            return;
        }
        await LookupAsync(cleaned);
    }

    private async Task LookupAsync(string isbn)
    {
        StatusLabel.Text = $"Looking up {isbn}…";
        FoundFrame.IsVisible = false;
        MissingFrame.IsVisible = false;
        WishlistFlag.IsVisible = false;
        // Reset the cover slot + the enriched-detail sections before
        // any new lookup — keeps stale content from the previous scan
        // off the new card while we wait for the new fetches (or
        // fall through to the placeholder / hidden sections).
        FoundCover.IsVisible = false;
        FoundCover.Source = null;
        FoundCoverPlaceholder.IsVisible = true;
        WorksSection.IsVisible = false;
        EditionsSection.IsVisible = false;
        WorksLayout.Children.Clear();
        EditionsLayout.Children.Clear();
        _coverCts?.Cancel();

        BookSnapshot? book;
        try
        {
            book = await _cache.LookupByIsbnAsync(isbn);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Lookup failed: {ex.GetType().Name} — {ex.Message}";
            return;
        }

        // Wishlist scan-flag — independent of whether the book is in
        // the library cache. Most useful in the MissingFrame branch
        // ("not owned, but on your wishlist — buy this!") but shown
        // in both branches for completeness. Best-effort: an error
        // here doesn't block the rest of the lookup.
        try
        {
            WishlistFlag.IsVisible = await _cache.IsWishlistedIsbnAsync(isbn);
        }
        catch
        {
            // Silent — the flag is gravy. The found/missing frame is
            // the load-bearing UI.
        }

        if (book is null)
        {
            StatusLabel.Text = "Scanned ✓";
            MissingIsbn.Text = $"ISBN: {isbn}";
            // Stash the ISBN on the Add button so the click handler
            // can deep-link with it pre-filled.
            AddToLibraryButton.CommandParameter = isbn;
            MissingFrame.IsVisible = true;
        }
        else
        {
            StatusLabel.Text = "Scanned ✓";
            FoundTitle.Text = book.Title;
            // Mirror the Web's WorkAuthorshipFormatter output:
            //   "Tolkien & Child; Sergio Cariello (illustrator)"
            // Falls back to PrimaryAuthor when AllAuthors is empty
            // (older server / malformed snapshot).
            var formatted = ContributorFormatter.Format(book.AllAuthors);
            FoundAuthors.Text = string.IsNullOrEmpty(formatted)
                ? book.PrimaryAuthor
                : formatted;
            FoundStatusRating.Text = FormatStatusRating(book);
            FoundIsbn.Text = $"ISBN: {isbn}";
            OpenInAppButton.CommandParameter = book.Id;
            FoundFrame.IsVisible = true;

            // Kick the cover fetch off without awaiting — the rest of
            // the card renders immediately and the cover swaps in
            // when ready (or stays as the placeholder if not).
            _coverCts = new CancellationTokenSource();
            var capturedToken = _coverCts.Token;
            var capturedBookId = book.Id;
            _ = LoadCoverAsync(capturedBookId, capturedToken);

            // Enriched detail (Works + Editions). Fire-and-forget so
            // the basic card renders without waiting for the SQLite
            // round-trip; the sections appear inline when ready.
            _ = LoadEnrichedDetailAsync(capturedBookId, capturedToken);
        }
    }

    private async Task LoadEnrichedDetailAsync(int bookId, CancellationToken ct)
    {
        BookEnrichedDetail? detail;
        try
        {
            detail = await _cache.GetBookEnrichedDetailAsync(bookId);
        }
        catch
        {
            // Enriched detail is gravy — failure to load it shouldn't
            // poison the main result card. Sections stay hidden.
            return;
        }
        if (detail is null || ct.IsCancellationRequested) return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (ct.IsCancellationRequested) return;

            // Works section: only meaningful for multi-Work compendiums.
            // Single-Work books would just echo the title we already
            // show — noise.
            if (detail.Works.Count > 1)
            {
                foreach (var work in detail.Works)
                {
                    WorksLayout.Children.Add(BuildWorkRow(work));
                }
                WorksSection.IsVisible = true;
            }

            // Editions section: only meaningful when the user owns
            // multiple editions of the Book. One-edition books would
            // just echo the cover/ISBN already shown in the top row.
            if (detail.Editions.Count > 1)
            {
                foreach (var edition in detail.Editions)
                {
                    EditionsLayout.Children.Add(BuildEditionRow(edition));
                }
                EditionsSection.IsVisible = true;
            }
        });
    }

    private static View BuildWorkRow(WorkSnapshot work)
    {
        // "Title — Author" per work; tight 13px so a 12-work compendium
        // doesn't overflow the FoundFrame visually. When the Work
        // carries non-Author contributors (illustrator on a co-authored
        // story, translator on a classic) the line surfaces them via
        // FormatContributors so the compendium row reads
        // "Title — Tolkien; Sergio Cariello (illustrator)". Falls back
        // to PrimaryAuthor when Contributors is null (older server) or
        // when the formatter yields nothing.
        var byline = work.Contributors is { Count: > 0 }
            ? ContributorFormatter.Format(work.Contributors)
            : null;
        if (string.IsNullOrEmpty(byline)) byline = work.PrimaryAuthor;

        return new Label
        {
            Text = string.IsNullOrWhiteSpace(byline)
                ? work.Title
                : $"{work.Title} — {byline}",
            FontSize = 13,
            TextColor = Color.FromArgb("#2C2416"),
            LineBreakMode = LineBreakMode.WordWrap,
        };
    }


    private View BuildEditionRow(EditionSnapshot edition)
    {
        // Two-column layout: small cover thumb + format/ISBN stack.
        // Smaller than the main FoundCover (40x60 vs 80x120) so the
        // section feels visually subordinate to the primary card.
        var coverPlaceholder = new Label
        {
            Text = "📖",
            FontSize = 16,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            TextColor = Color.FromArgb("#A67B3A"),
        };
        var coverImage = new Image
        {
            IsVisible = false,
            Aspect = Aspect.AspectFit,
        };
        var coverSlot = new Grid
        {
            WidthRequest = 40,
            HeightRequest = 60,
            BackgroundColor = Color.FromArgb("#E0DAC8"),
            VerticalOptions = LayoutOptions.Start,
            Children = { coverPlaceholder, coverImage },
        };

        // "Hardcover · 3rd ed." when EditionNumber is set; just the
        // format name otherwise. The ordinal suffix is local because
        // the only other consumer (Detail.razor) has its own copy —
        // not worth a shared helper for 6 lines.
        var formatText = edition.EditionNumber is int n
            ? $"{FormatPrettyName(edition.Format)} · {FormatOrdinalEdition(n)}"
            : FormatPrettyName(edition.Format);
        var formatLabel = new Label
        {
            Text = formatText,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#2C2416"),
        };
        var isbnLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(edition.Isbn) ? "no ISBN" : edition.Isbn,
            FontSize = 11,
            FontFamily = "Courier New",
            TextColor = Color.FromArgb("#6B5D4A"),
        };

        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { formatLabel, isbnLabel },
        };

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 10,
        };
        row.Add(coverSlot, 0, 0);
        row.Add(textStack, 1, 0);

        // Per-edition cover fetch — uses the edition's CoverUrl (the
        // edition-specific cover, distinct from Book.DefaultCoverArtUrl).
        // Cache miss / no-URL silently leaves the placeholder. We
        // route through HttpClient directly rather than the cache's
        // EnsureCoverCachedAsync — that one keys on Book.Id and would
        // overwrite the main cover. For now, edition thumbs are a
        // best-effort in-memory fetch with no disk cache; revisit if
        // re-fetching every scan becomes a problem.
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
        catch
        {
            // Placeholder stays. No retry layer — the section is
            // best-effort gravy on top of the main card.
        }
    }

    private static string FormatPrettyName(string format)
    {
        // BookFormat enum names are PascalCase (MassMarketPaperback,
        // TradePaperback, etc) — render with a space before each
        // capital so they read naturally.
        if (string.IsNullOrEmpty(format)) return "(unknown)";
        var sb = new System.Text.StringBuilder(format.Length + 4);
        for (int i = 0; i < format.Length; i++)
        {
            if (i > 0 && char.IsUpper(format[i]) && !char.IsUpper(format[i - 1]))
            {
                sb.Append(' ');
            }
            sb.Append(format[i]);
        }
        return sb.ToString();
    }

    private static string FormatOrdinalEdition(int n) =>
        BookTracker.Shared.Formatting.OrdinalFormatter.OrdinalEdition(n);

    private async Task LoadCoverAsync(int bookId, CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient("covers");
            var path = await _cache.EnsureCoverCachedAsync(bookId, http, ct);
            if (path is null || ct.IsCancellationRequested) return;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Guard against the user having scanned a different
                // book in the time it took for this fetch to land.
                if (ct.IsCancellationRequested) return;
                FoundCover.Source = ImageSource.FromFile(path);
                FoundCover.IsVisible = true;
                FoundCoverPlaceholder.IsVisible = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Replaced by a later scan; leave the placeholder in place.
        }
    }

    private static string FormatStatusRating(BookSnapshot book)
    {
        var parts = new List<string>();
        if (book.Rating > 0)
        {
            var filled = new string('★', Math.Clamp(book.Rating, 0, 5));
            var empty = new string('☆', 5 - Math.Clamp(book.Rating, 0, 5));
            parts.Add(filled + empty);
        }
        if (!string.IsNullOrWhiteSpace(book.Status))
        {
            parts.Add(book.Status);
        }
        return parts.Count == 0 ? "" : string.Join("  ·  ", parts);
    }

    private async void OnOpenInAppClicked(object? sender, EventArgs e)
    {
        // Deep-link to the Web app's book detail page. Requires the
        // user to be online; mobile companion is read-only in v1.
        if (sender is Button { CommandParameter: int bookId })
        {
            var url = $"{AppConfig.ApiBaseUrl}/books/{bookId}";
            try { await Launcher.Default.OpenAsync(new Uri(url)); }
            catch (Exception ex) { StatusLabel.Text = $"Couldn't open browser: {ex.Message}"; }
        }
    }

    private async void OnAddToLibraryClicked(object? sender, EventArgs e)
    {
        // Same online-required deep link, into the Add page. PR 5
        // doesn't pre-fill the ISBN on the Web side; the user types
        // it in after the page loads. A future Web-side change could
        // accept ?isbn= as a query param.
        var url = $"{AppConfig.ApiBaseUrl}/books/add";
        try { await Launcher.Default.OpenAsync(new Uri(url)); }
        catch (Exception ex) { StatusLabel.Text = $"Couldn't open browser: {ex.Message}"; }
    }
}
