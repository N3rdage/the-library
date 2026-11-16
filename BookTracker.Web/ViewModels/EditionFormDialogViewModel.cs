using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Formatting;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.Services.Covers;
using BookTracker.Web.Validation;
using BookTracker.Web.Components.Shared;
using Microsoft.EntityFrameworkCore;

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
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IBookLookupService lookup,
    IDispatcher dispatcher)
{
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

    public record PublisherOption(int Id, string Name);

    public string? LookupMessage { get; private set; }
    public bool LookingUp { get; private set; }

    public async Task InitializeForAddAsync(int bookId)
    {
        IsNew = true;
        BookId = bookId;
        EditionId = null;

        await using var db = await dbFactory.CreateDbContextAsync();
        ExistingPublishers = await LoadPublishersAsync(db);
    }

    public async Task InitializeForEditAsync(int editionId)
    {
        IsNew = false;
        EditionId = editionId;

        await using var db = await dbFactory.CreateDbContextAsync();
        var edition = await db.Editions.Include(e => e.Publisher).FirstOrDefaultAsync(e => e.Id == editionId);
        if (edition is null) { NotFound = true; return; }

        BookId = edition.BookId;
        Isbn = edition.Isbn;
        Format = edition.Format;
        Publisher = edition.Publisher?.Name;
        FirstPublishedOrPrintedDate = PartialDateParser.Format(edition.DatePrinted, edition.DatePrintedPrecision);
        LoadCoverUrl(edition.CoverUrl);
        ExistingPublishers = await LoadPublishersAsync(db);
    }

    private static Task<List<PublisherOption>> LoadPublishersAsync(BookTrackerDbContext db) =>
        db.Publishers
            .OrderBy(p => p.Name)
            .Select(p => new PublisherOption(p.Id, p.Name))
            .ToListAsync();

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

    /// <returns>The new or updated Edition id.</returns>
    /// <returns>The new or updated Edition id; null if the dialog state is stale
    /// (book/edition gone). Publisher find-or-create now lives in the handler.</returns>
    public async Task<int?> SaveAsync()
    {
        if (NotFound || !CoverUrlAcceptable) return null;

        var datePrinted = PartialDateParser.TryParse(FirstPublishedOrPrintedDate) ?? PartialDate.Empty;
        var coverUrl = string.IsNullOrWhiteSpace(CoverUrl) ? null : CoverUrl.Trim();
        try
        {
            if (IsNew)
            {
                return await dispatcher.Send(new AddEditionToBook(
                    BookId, Isbn, Format, datePrinted.Date, datePrinted.Precision,
                    Publisher, coverUrl, FirstCopyCondition));
            }

            if (EditionId is not int id) return null;
            await dispatcher.Send(new UpdateEdition(
                id, Isbn, Format, datePrinted.Date, datePrinted.Precision, Publisher, coverUrl));
            return id;
        }
        catch (NotFoundException)
        {
            // Book/Edition deleted between opening the dialog and saving — no-op,
            // matching the old FindAsync-returns-null path.
            return null;
        }
    }
}
