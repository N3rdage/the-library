using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Formatting;
using BookTracker.Application.Series;
using BookTracker.Data.Models;
using BookTracker.Web.Services.Covers;
using BookTracker.Web.Validation;

namespace BookTracker.Web.ViewModels;

// Dialog-scoped VM for "Edit book details" on the View page. Covers
// Book-level fields that sit apart from the Work (title, category,
// cover URL) plus series membership + order — the Book is installment N
// of a publication series. Notes have inline auto-save on the View page
// and are deliberately absent here. Genres live on the Work and are
// edited via WorkEditDialogViewModel.
public class BookEditDialogViewModel(IDispatcher dispatcher) : DispatchingViewModel
{
    public bool NotFound { get; private set; }
    public int BookId { get; private set; }

    public string Title { get; set; } = "";
    public BookCategory Category { get; set; }
    public string? CoverUrl { get; set; }

    // The cover URL as loaded. A stored URL (an Azurite blob URL in local dev, a
    // row that predates the rule) stays saveable while unchanged; only an edited
    // URL has to be one the cover mirror can fetch.
    private string? _loadedCoverUrl;

    // Save stays disabled while this is false.
    public bool CoverUrlAcceptable => IsCoverUrlAcceptable(CoverUrl);

    public bool IsCoverUrlAcceptable(string? url) =>
        string.Equals(url?.Trim(), _loadedCoverUrl?.Trim(), StringComparison.Ordinal)
        || CoverFetchPolicy.IsAcceptableCoverUrl(url);

    // The cover URL field's Validation func: the field message, or null when acceptable.
    public string? CoverUrlError(string? url) => IsCoverUrlAcceptable(url) ? null : CoverUrlAttribute.Message;

    internal void LoadCoverUrl(string? url)
    {
        CoverUrl = url;
        _loadedCoverUrl = url;
    }

    public int? SelectedSeriesId { get; set; }
    // Free-text so the user can enter "4.5" interquels / "1A" hierarchical
    // positions — parsed into (SeriesOrder int sort key, SeriesOrderDisplay
    // override) on save via SeriesOrderParser.
    public string? SeriesOrderInput { get; set; }

    public IReadOnlyList<SeriesPickerOption> AvailableSeries { get; private set; } = [];

    public async Task InitializeAsync(int bookId)
    {
        BookId = bookId;
        var book = await dispatcher.Query(new GetBookEditForm(bookId));
        if (book is null) { NotFound = true; return; }

        Title = book.Title;
        Category = book.Category;
        LoadCoverUrl(book.DefaultCoverArtUrl);
        SelectedSeriesId = book.SeriesId;
        // Only surface an order when the book is actually in a series. A series
        // delete SET NULLs SeriesId but leaves SeriesOrder behind, so a stale
        // order must not pre-fill the field and silently ride into the next
        // series the user picks.
        SeriesOrderInput = book.SeriesId is null
            ? null
            : SeriesOrderParser.Format(book.SeriesOrder, book.SeriesOrderDisplay);

        AvailableSeries = await dispatcher.Query(new GetSeriesOptions());
    }

    // The series dropdown routes through here (Value/ValueChanged, not two-way
    // @bind) so switching series RESETS the order: the order belonged to the
    // previous series and must not ride into the newly-picked one — a Witcher #5
    // book switched to "Dune" without touching the order field would otherwise
    // save as Dune #5 and claim a phantom slot. Re-picking the same value is a
    // no-op (leaves the loaded order intact).
    public void OnSeriesChanged(int? newSeriesId)
    {
        if (newSeriesId == SelectedSeriesId) return;
        SelectedSeriesId = newSeriesId;
        SeriesOrderInput = null;
    }

    /// <summary>True when saved; false when the dialog state is stale or the
    /// domain refused (see <see cref="DispatchingViewModel.Error"/>).</summary>
    public async Task<bool> SaveAsync()
    {
        if (NotFound || string.IsNullOrWhiteSpace(Title) || !CoverUrlAcceptable) return false;

        int? seriesOrder = null;
        string? seriesOrderDisplay = null;
        if (SelectedSeriesId.HasValue)
            (seriesOrder, seriesOrderDisplay) = SeriesOrderParser.Parse(SeriesOrderInput);

        return await DispatchAsync(() => dispatcher.Send(new UpdateBookDetails(
            BookId, Title, Category, string.IsNullOrWhiteSpace(CoverUrl) ? null : CoverUrl.Trim(),
            SelectedSeriesId, seriesOrder, seriesOrderDisplay)));
    }
}
