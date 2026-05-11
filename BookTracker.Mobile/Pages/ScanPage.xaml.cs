using BookTracker.Mobile.Cache;
using BookTracker.Shared.Catalog;
using ZXing.Net.Maui;

namespace BookTracker.Mobile.Pages;

public partial class ScanPage : ContentPage
{
    private readonly ICatalogCache _cache;

    // Debounce — barcode scanners fire detect events ~10x/sec while
    // the code is in frame. We only want one lookup per "real" scan,
    // and re-pointing at the same book shouldn't immediately re-fire.
    // Mirrors the 3s window from BookTracker.Web/wwwroot/js/barcode-scanner.js.
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(3);
    private string? _lastScannedIsbn;
    private DateTime _lastScannedAt = DateTime.MinValue;

    public ScanPage(ICatalogCache cache)
    {
        InitializeComponent();
        _cache = cache;

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
            FoundAuthors.Text = (book.AllAuthors is { Count: > 1 })
                ? string.Join(", ", book.AllAuthors)
                : book.PrimaryAuthor;
            FoundStatusRating.Text = FormatStatusRating(book);
            FoundIsbn.Text = $"ISBN: {isbn}";
            OpenInAppButton.CommandParameter = book.Id;
            FoundFrame.IsVisible = true;
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
