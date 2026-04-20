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
                        .ThenInclude(b => b.Works).ThenInclude(w => w.Author)
                    .Include(e => e.Copies)
                    .FirstOrDefaultAsync(e => e.Isbn == cleanIsbn);

                if (existing is not null)
                {
                    ExistingBook = new ExistingBookMatch(
                        existing.Book.Id,
                        existing.Id,
                        existing.Book.Title,
                        string.Join(", ", existing.Book.Works.Select(w => w.Author.Name).Distinct()),
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
            if (string.IsNullOrWhiteSpace(WorkInput.Author)) WorkInput.Author = result.Author ?? "";
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

            SeriesSuggestion = await seriesMatch.FindMatchAsync(result.Title, result.Author);
            SeriesSuggestionDismissed = false;
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
        if (string.IsNullOrWhiteSpace(WorkInput.Author)) WorkInput.Author = candidate.Author ?? "";
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

            var author = await AuthorResolver.FindOrCreateAsync(WorkInput.Author!, db);
            var firstPub = PartialDateParser.TryParse(WorkInput.FirstPublishedDate) ?? PartialDate.Empty;
            var work = new Work
            {
                Title = (WorkInput.Title ?? BookInput.Title)!.Trim(),
                Subtitle = string.IsNullOrWhiteSpace(WorkInput.Subtitle) ? null : WorkInput.Subtitle!.Trim(),
                Author = author,
                FirstPublishedDate = firstPub.Date,
                FirstPublishedDatePrecision = firstPub.Precision,
                Genres = selectedGenres,
            };

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
