using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Formatting;
using BookTracker.Application.Publishers;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.Services.Covers;
using BookTracker.Web.Validation;
using BookTracker.Web.Components.Shared;

namespace BookTracker.Web.ViewModels;

// Dialog VM for Add Edition and Edit Edition, picked by IsNew flag.
//
// Add:
//   InitializeForAddAsync(bookId) — blank form, lookup button available,
//   FirstCopyCondition captured so save creates Edition + first Copy.
// Edit:
//   InitializeForEditAsync(editionId) — prefills existing fields, no
//   lookup, no first-copy field. Save updates in place.
//
// Publisher is typeahead find-or-create (same shape as Author on the
// Work dialog). ISBN is optional (nullable, filtered unique) so
// pre-1974 editions save without one.
public class EditionFormDialogViewModel(
    IBookLookupService lookup,
    IDispatcher dispatcher,
    CoverPhotoUploader coverUploader) : DispatchingViewModel
{
    /// <summary>A cropped cover photo uploaded after the edition saves; it wins over the cover URL field.</summary>
    public CoverPhoto? CoverPhoto { get; set; }

    /// <summary>Set when the edition saved but its cover photo did not.</summary>
    public string? CoverUploadError { get; private set; }

    public bool IsNew { get; private set; }
    public bool NotFound { get; private set; }
    public int BookId { get; private set; }
    public int? EditionId { get; private set; }

    public string? Isbn { get; set; }
    public BookFormat Format { get; set; } = BookFormat.TradePaperback;
    public string? Publisher { get; set; }
    public string FirstPublishedOrPrintedDate { get; set; } = "";
    public string? CoverUrl { get; set; }

    // The cover URL as loaded (null on Add). A stored URL (an Azurite blob URL in
    // local dev, a row that predates the rule) stays saveable while unchanged;
    // only an edited URL has to be one the cover mirror can fetch.
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

    // Add-only: the first Copy that ships with a new Edition. Every
    // Edition needs at least one Copy, so the Add flow captures it here.
    public BookCondition FirstCopyCondition { get; set; } = BookCondition.Good;

    // Publisher lookup cached client-side (loaded once on init). Publishers
    // are a small lookup table, so the autocomplete filters this in-memory
    // rather than round-tripping the DB per keystroke. It also lets the
    // commit handler tell an existing pick (already in this list → no create
    // call) from a genuinely new name (TD-15a eager create). A newly-created
    // publisher is appended here so it's known to subsequent commits + shows
    // in suggestions.
    public List<PublisherOption> ExistingPublishers { get; private set; } = [];

    public string? LookupMessage { get; private set; }
    public bool LookingUp { get; private set; }

    public async Task InitializeForAddAsync(int bookId)
    {
        IsNew = true;
        BookId = bookId;
        EditionId = null;

        ExistingPublishers = await LoadPublishersAsync();
    }

    public async Task InitializeForEditAsync(int editionId)
    {
        IsNew = false;
        EditionId = editionId;

        var edition = await dispatcher.Query(new GetEditionForEdit(editionId));
        if (edition is null) { NotFound = true; return; }

        BookId = edition.BookId;
        Isbn = edition.Isbn;
        Format = edition.Format;
        Publisher = edition.PublisherName;
        FirstPublishedOrPrintedDate = PartialDateParser.Format(edition.DatePrinted, edition.DatePrintedPrecision);
        LoadCoverUrl(edition.CoverUrl);
        ExistingPublishers = await LoadPublishersAsync();
    }

    // A mutable copy: a publisher created from the dialog is appended to it.
    private async Task<List<PublisherOption>> LoadPublishersAsync() =>
        [.. await dispatcher.Query(new GetPublisherOptions())];

    public Task<IEnumerable<string>> SearchPublishersAsync(string query, CancellationToken ct)
    {
        // Filters the cached ExistingPublishers in-memory (loaded once on init)
        // rather than hitting the DB per keystroke. Shared trim/Contains/cap rule.
        return Task.FromResult(LookupSearch.Filter(ExistingPublishers.Select(p => p.Name), query));
    }

    public async Task LookupAsync()
    {
        LookupMessage = null;
        if (string.IsNullOrWhiteSpace(Isbn))
        {
            LookupMessage = "Enter an ISBN to look up.";
            return;
        }

        LookingUp = true;
        try
        {
            var result = await lookup.LookupByIsbnAsync(Isbn, CancellationToken.None);
            if (result is null)
            {
                LookupMessage = $"No match found for ISBN {Isbn}.";
                return;
            }

            // Auto-fill empties only — don't clobber anything the user typed.
            if (string.IsNullOrWhiteSpace(Isbn)) Isbn = result.Isbn;
            if (string.IsNullOrWhiteSpace(Publisher)) Publisher = result.Publisher;
            if (string.IsNullOrWhiteSpace(CoverUrl)) CoverUrl = result.CoverUrl;
            if (string.IsNullOrWhiteSpace(FirstPublishedOrPrintedDate) && result.DatePrinted is DateOnly d)
            {
                FirstPublishedOrPrintedDate = PartialDateParser.Format(d, result.DatePrintedPrecision);
            }
            if (result.Format is BookFormat fmt) Format = fmt;

            LookupMessage = $"Prefilled from {result.Source}. Edit anything before saving.";
        }
        finally
        {
            LookingUp = false;
        }
    }

    /// <returns>The new or updated Edition id; null if the dialog state is stale
    /// or the domain refused (see <see cref="DispatchingViewModel.Error"/>).
    /// Publisher find-or-create lives in the handler.</returns>
    public async Task<int?> SaveAsync()
    {
        CoverUploadError = null;
        if (NotFound || !CoverUrlAcceptable) return null;

        var id = await SaveEditionAsync();
        if (id is int savedId && CoverPhoto is not null)
        {
            await AttachCoverPhotoAsync(savedId, CoverPhoto);
        }
        return id;
    }

    private async Task<int?> SaveEditionAsync()
    {
        var datePrinted = PartialDateParser.TryParse(FirstPublishedOrPrintedDate) ?? PartialDate.Empty;
        var coverUrl = string.IsNullOrWhiteSpace(CoverUrl) ? null : CoverUrl.Trim();
        if (IsNew)
        {
            return await DispatchAsync<int?>(async () => await dispatcher.Send(new AddEditionToBook(
                BookId, Isbn, Format, datePrinted.Date, datePrinted.Precision,
                Publisher, coverUrl, FirstCopyCondition)));
        }

        if (EditionId is not int id) return null;
        return await DispatchAsync(() => dispatcher.Send(new UpdateEdition(
            id, Isbn, Format, datePrinted.Date, datePrinted.Precision, Publisher, coverUrl)))
            ? id
            : null;
    }

    // Runs after the edition is saved, because UpdateEdition writes CoverUrl and the
    // photo has to win. A failure leaves the edition saved and is reported through
    // CoverUploadError.
    private async Task AttachCoverPhotoAsync(int editionId, CoverPhoto photo)
    {
        var upload = await coverUploader.UploadAsync(photo.Bytes, photo.ContentType, editionId, CancellationToken.None);
        if (!upload.Success)
        {
            CoverUploadError = upload.ErrorMessage;
            return;
        }

        if (!await DispatchAsync(() => dispatcher.Send(new SetEditionCover(editionId, upload.NewUrl!, IsUserSupplied: true))))
        {
            CoverUploadError = Error;
            Error = null;
        }
    }
}
