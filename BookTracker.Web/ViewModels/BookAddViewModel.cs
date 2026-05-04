using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class BookAddViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IBookLookupService lookup,
    SeriesMatchService seriesMatch)
{
    public BookFormViewModel.BookFormInput BookInput { get; set; } = new();
    public WorkFormViewModel.WorkFormInput WorkInput { get; set; } = new();
    public EditionFormViewModel.EditionFormInput EditionInput { get; set; } = new();
    public CopyFormViewModel.CopyFormInput CopyInput { get; set; } = new();
    public List<string> LookupCandidates { get; private set; } = [];

    public string? LookupIsbn { get; set; }
    public string? LookupMessage { get; private set; }
    public bool LookingUp { get; private set; }
    public bool Saving { get; private set; }

    // No-ISBN flow (web only — for pre-1974 books that predate ISBN).
    // Toggling NoIsbnMode swaps the lookup panel from ISBN entry to a
    // title/author search that returns work-level candidates from Open
    // Library; selecting one prefills the form like an ISBN lookup would.
    public bool NoIsbnMode { get; set; }
    public string? SearchTitle { get; set; }
    public string? SearchAuthor { get; set; }
    public IReadOnlyList<BookSearchCandidate> SearchCandidates { get; private set; } = [];
    public bool Searching { get; private set; }
    public string? SearchMessage { get; private set; }

    public SeriesMatch? SeriesSuggestion { get; private set; }
    public bool SeriesSuggestionDismissed { get; set; }

    // Acceptance state for the series suggestion banner. When the user clicks
    // Accept, we capture the suggestion's identity (existing SeriesId, or a
    // proposed name to find-or-create on save) plus the suggested order. The
    // save path reads these and attaches the Work to the right Series row.
    // Cleared on Reset() and on a fresh successful lookup so accept-state
    // can't bleed across captures.
    public bool SeriesSuggestionAccepted { get; private set; }
    public int? AcceptedSeriesId { get; private set; }
    public string? AcceptedSeriesName { get; private set; }
    public int? AcceptedSeriesOrder { get; private set; }

    public void AcceptSeriesSuggestion()
    {
        if (SeriesSuggestion is null) return;
        // Only API-sourced suggestions (Existing or NewSeries) are actionable —
        // the local-fallback paths name no concrete series to attach. The UI
        // should only render an Accept button for those reasons.
        if (SeriesSuggestion.Reason is not (MatchReason.ApiMatchExisting or MatchReason.ApiMatchNewSeries))
        {
            return;
        }
        AcceptedSeriesId = SeriesSuggestion.SeriesId;
        AcceptedSeriesName = SeriesSuggestion.SeriesName;
        AcceptedSeriesOrder = SeriesSuggestion.SuggestedOrder;
        SeriesSuggestionAccepted = true;
    }

    public void UndoSeriesSuggestionAccept()
    {
        SeriesSuggestionAccepted = false;
        AcceptedSeriesId = null;
        AcceptedSeriesName = null;
        AcceptedSeriesOrder = null;
    }

    // Existing-book detection — set during LookupAsync when the ISBN
    // already maps to an Edition in the library. The Add page surfaces a
    // banner offering "add another copy" / "edit existing" instead of
    // letting the user accidentally hit the unique-ISBN constraint by
    // saving a duplicate.
    public ExistingBookMatch? ExistingBook { get; private set; }
    public bool AddingCopy { get; private set; }

    public async Task LookupAsync(GenrePickerViewModel genrePicker)
    {
        LookupMessage = null;
        ExistingBook = null;
        if (string.IsNullOrWhiteSpace(LookupIsbn))
        {
            LookupMessage = "Enter an ISBN to look up.";
            return;
        }

        LookingUp = true;
        try
        {
            // Existing-edition check first — if the ISBN already maps to a
            // Book in the library, surface "add another copy" instead of
            // letting the user save a duplicate that would hit the unique
            // ISBN constraint.
            var cleanIsbn = new string(LookupIsbn.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                var existing = await db.Editions
                    .Include(e => e.Book)
                        .ThenInclude(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
                    .Include(e => e.Copies)
                    .FirstOrDefaultAsync(e => e.Isbn == cleanIsbn);

                if (existing is not null)
                {
                    ExistingBook = new ExistingBookMatch(
                        existing.Book.Id,
                        existing.Id,
                        existing.Book.Title,
                        string.Join(", ", existing.Book.Works
                            .SelectMany(w => w.WorkAuthors.OrderBy(wa => wa.Order).Select(wa => wa.Author.Name))
                            .Distinct()),
                        existing.Copies.Count);
                    LookupMessage = null;
                    return;
                }
            }

            var result = await lookup.LookupByIsbnAsync(LookupIsbn, CancellationToken.None);
            if (result is null)
            {
                LookupMessage = $"No match found for ISBN {LookupIsbn}.";
                return;
            }

            // The Add page creates a Book with one Work. Lookup result
            // populates both: Book.Title mirrors the Work title, plus
            // cover; Work gets title/subtitle/author/genres.
            if (string.IsNullOrWhiteSpace(BookInput.Title)) BookInput.Title = result.Title ?? "";
            if (string.IsNullOrWhiteSpace(BookInput.DefaultCoverArtUrl)) BookInput.DefaultCoverArtUrl = result.CoverUrl;
            if (string.IsNullOrWhiteSpace(WorkInput.Title)) WorkInput.Title = result.Title ?? "";
            if (string.IsNullOrWhiteSpace(WorkInput.Subtitle)) WorkInput.Subtitle = result.Subtitle;
            // Lookup gives a single author string — seed the chip list with it
            // when empty. User can add additional co-authors via the picker
            // before saving.
            if (WorkInput.Authors.Count == 0 && !string.IsNullOrWhiteSpace(result.Author))
            {
                WorkInput.Authors = [result.Author];
            }
            if (string.IsNullOrWhiteSpace(EditionInput.Isbn)) EditionInput.Isbn = result.Isbn;
            if (string.IsNullOrWhiteSpace(EditionInput.Publisher)) EditionInput.Publisher = result.Publisher;
            if (string.IsNullOrWhiteSpace(EditionInput.DatePrinted) && result.DatePrinted is DateOnly d)
            {
                EditionInput.DatePrinted = PartialDateParser.Format(d, result.DatePrintedPrecision);
            }

            LookupCandidates = result.GenreCandidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            genrePicker.LookupCandidates = LookupCandidates;
            genrePicker.ApplyLookupCandidates(LookupCandidates);

            LookupMessage = $"Prefilled from {result.Source}. Edit anything before saving.";

            SeriesSuggestion = await seriesMatch.FindMatchAsync(result);
            SeriesSuggestionDismissed = false;
            // Fresh lookup → clear any acceptance carried over from a prior
            // ISBN attempt in this session (e.g. user typed wrong ISBN,
            // accepted a Discworld suggestion, then corrected to a non-series
            // book — the prior accept must not silently apply).
            UndoSeriesSuggestionAccept();
        }
        finally
        {
            LookingUp = false;
        }
    }

    /// <summary>
    /// Adds a new Copy to the Edition flagged in <see cref="ExistingBook"/>.
    /// Used by the "you already own this book" banner so re-scanning a
    /// barcode for a second physical copy is a one-click action.
    /// </summary>
    /// <returns>The book id of the existing book the copy was attached to.</returns>
    public async Task<int?> AddCopyToExistingAsync()
    {
        if (ExistingBook is null) return null;

        AddingCopy = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var newCopy = new Copy
            {
                EditionId = ExistingBook.EditionId,
                Condition = CopyInput.Condition,
            };
            db.Copies.Add(newCopy);
            await db.SaveChangesAsync();
            return ExistingBook.BookId;
        }
        finally
        {
            AddingCopy = false;
        }
    }

    /// <summary>Resets all input state so the page is ready for the next book.</summary>
    public void Reset(GenrePickerViewModel genrePicker)
    {
        BookInput = new();
        WorkInput = new();
        EditionInput = new();
        CopyInput = new();
        LookupIsbn = null;
        LookupMessage = null;
        LookupCandidates = [];
        ExistingBook = null;
        SeriesSuggestion = null;
        SeriesSuggestionDismissed = false;
        UndoSeriesSuggestionAccept();
        SearchTitle = null;
        SearchAuthor = null;
        SearchCandidates = [];
        SearchMessage = null;
        NoIsbnMode = false;
        genrePicker.SelectedGenreIds = [];
        genrePicker.LookupCandidates = [];
    }

    public record ExistingBookMatch(int BookId, int EditionId, string Title, string Author, int CopyCount);

    public async Task SearchAsync()
    {
        SearchMessage = null;
        SearchCandidates = [];

        var t = SearchTitle?.Trim();
        var a = SearchAuthor?.Trim();
        if (string.IsNullOrEmpty(t) && string.IsNullOrEmpty(a))
        {
            SearchMessage = "Enter a title or author to search.";
            return;
        }

        Searching = true;
        try
        {
            SearchCandidates = await lookup.SearchByTitleAuthorAsync(t, a, CancellationToken.None);
            if (SearchCandidates.Count == 0)
            {
                SearchMessage = "No matches found. Try different keywords or fill the form manually.";
            }
        }
        finally
        {
            Searching = false;
        }
    }

    public async Task ApplyCandidateAsync(BookSearchCandidate candidate, GenrePickerViewModel genrePicker)
    {
        if (string.IsNullOrWhiteSpace(BookInput.Title)) BookInput.Title = candidate.Title ?? "";
        if (string.IsNullOrWhiteSpace(BookInput.DefaultCoverArtUrl)) BookInput.DefaultCoverArtUrl = candidate.CoverUrl;
        if (string.IsNullOrWhiteSpace(WorkInput.Title)) WorkInput.Title = candidate.Title ?? "";
        if (WorkInput.Authors.Count == 0 && !string.IsNullOrWhiteSpace(candidate.Author))
        {
            WorkInput.Authors = [candidate.Author];
        }
        // first_publish_year is the WORK's first year — perfect fit for
        // Work.FirstPublishedDate; not the edition's print date. We only
        // know the year so format it as such.
        if (string.IsNullOrWhiteSpace(WorkInput.FirstPublishedDate) && candidate.FirstPublishYear is int year)
        {
            WorkInput.FirstPublishedDate = year.ToString();
        }

        SearchMessage = $"Prefilled from Open Library. Fill in format, exact print date, and publisher from the book in hand.";

        SeriesSuggestion = await seriesMatch.FindMatchAsync(candidate.Title, candidate.Author);
        SeriesSuggestionDismissed = false;
        UndoSeriesSuggestionAccept();

        // No genre auto-pick for the no-ISBN flow — search results don't
        // carry subjects. User selects genres manually via the picker.
        _ = genrePicker;
    }

    public async Task<int?> SaveAsync(List<int> selectedGenreIds)
    {
        Saving = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var selectedGenres = await db.Genres
                .Where(g => selectedGenreIds.Contains(g.Id))
                .ToListAsync();

            Publisher? publisher = null;
            var publisherName = EditionInput.Publisher?.Trim();
            if (!string.IsNullOrEmpty(publisherName))
            {
                publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == publisherName);
                if (publisher is null)
                {
                    publisher = new Publisher { Name = publisherName };
                    db.Publishers.Add(publisher);
                }
            }

            var authors = await AuthorResolver.FindOrCreateAllAsync(WorkInput.Authors, db);
            if (authors.Count == 0)
            {
                throw new InvalidOperationException("At least one author is required to save a Work.");
            }
            var firstPub = PartialDateParser.TryParse(WorkInput.FirstPublishedDate) ?? PartialDate.Empty;
            var work = new Work
            {
                Title = (WorkInput.Title ?? BookInput.Title)!.Trim(),
                Subtitle = string.IsNullOrWhiteSpace(WorkInput.Subtitle) ? null : WorkInput.Subtitle!.Trim(),
                FirstPublishedDate = firstPub.Date,
                FirstPublishedDatePrecision = firstPub.Precision,
                Genres = selectedGenres,
            };
            // Dual-write: Work.Author = lead chip (legacy FK compat); Work.WorkAuthors
            // = all chips with Order ascending. PR2 will drop Author/AuthorId and
            // switch reads to the join.
            AuthorResolver.AssignAuthors(work, authors);

            // Attach to the accepted series, if any. AcceptedSeriesId points at
            // an existing local Series row; AcceptedSeriesName (without an Id)
            // means the upstream API named a series we don't have yet — find-
            // or-create it by name. Default new series to SeriesType.Series
            // (numbered) per the Q1/Q2 defaults — user can flip to Collection
            // on /series/{id} later if wrong.
            if (SeriesSuggestionAccepted)
            {
                if (AcceptedSeriesId is int existingId)
                {
                    work.SeriesId = existingId;
                    work.SeriesOrder = AcceptedSeriesOrder;
                }
                else if (!string.IsNullOrWhiteSpace(AcceptedSeriesName))
                {
                    var seriesName = AcceptedSeriesName.Trim();
                    var series = await db.Series
                        .FirstOrDefaultAsync(s => s.Name.ToLower() == seriesName.ToLower());
                    if (series is null)
                    {
                        series = new Series { Name = seriesName, Type = SeriesType.Series };
                        db.Series.Add(series);
                    }
                    work.Series = series;
                    work.SeriesOrder = AcceptedSeriesOrder;
                }
            }

            var datePrinted = PartialDateParser.TryParse(EditionInput.DatePrinted) ?? PartialDate.Empty;
            var book = new Book
            {
                Title = BookInput.Title!.Trim(),
                Notes = string.IsNullOrWhiteSpace(BookInput.Notes) ? null : BookInput.Notes.Trim(),
                Category = BookInput.Category,
                Status = BookInput.Status,
                Rating = BookInput.Rating,
                DefaultCoverArtUrl = string.IsNullOrWhiteSpace(BookInput.DefaultCoverArtUrl) ? null : BookInput.DefaultCoverArtUrl.Trim(),
                Works = [work],
                Editions =
                [
                    new Edition
                    {
                        Isbn = string.IsNullOrWhiteSpace(EditionInput.Isbn) ? null : EditionInput.Isbn.Trim(),
                        Format = EditionInput.Format,
                        DatePrinted = datePrinted.Date,
                        DatePrintedPrecision = datePrinted.Precision,
                        Publisher = publisher,
                        CoverUrl = string.IsNullOrWhiteSpace(EditionInput.CoverUrl) ? null : EditionInput.CoverUrl.Trim(),
                        Copies = [new Copy { Condition = CopyInput.Condition }]
                    }
                ]
            };

            db.Books.Add(book);
            await db.SaveChangesAsync();
            return book.Id;
        }
        finally
        {
            Saving = false;
        }
    }
}
