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

    public async Task LookupAsync(GenrePickerViewModel genrePicker)
    {
        LookupMessage = null;
        if (string.IsNullOrWhiteSpace(LookupIsbn))
        {
            LookupMessage = "Enter an ISBN to look up.";
            return;
        }

        LookingUp = true;
        try
        {
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
            EditionInput.DatePrinted ??= result.DatePrinted;

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
        // Work.FirstPublishedDate; not the edition's print date.
        if (WorkInput.FirstPublishedDate is null && candidate.FirstPublishYear is int year)
        {
            WorkInput.FirstPublishedDate = new DateOnly(year, 1, 1);
        }

        SearchMessage = $"Prefilled from Open Library. Fill in format, exact print date, and publisher from the book in hand.";

        SeriesSuggestion = await seriesMatch.FindMatchAsync(candidate.Title, candidate.Author);
        SeriesSuggestionDismissed = false;

        // No genre auto-pick for the no-ISBN flow — search results don't
        // carry subjects. User selects genres manually via the picker.
        _ = genrePicker;
    }

    public async Task<bool> SaveAsync(List<int> selectedGenreIds)
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
            var work = new Work
            {
                Title = (WorkInput.Title ?? BookInput.Title)!.Trim(),
                Subtitle = string.IsNullOrWhiteSpace(WorkInput.Subtitle) ? null : WorkInput.Subtitle!.Trim(),
                Author = author,
                FirstPublishedDate = WorkInput.FirstPublishedDate,
                Genres = selectedGenres,
            };

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
                        DatePrinted = EditionInput.DatePrinted,
                        Publisher = publisher,
                        CoverUrl = string.IsNullOrWhiteSpace(EditionInput.CoverUrl) ? null : EditionInput.CoverUrl.Trim(),
                        Copies = [new Copy { Condition = CopyInput.Condition }]
                    }
                ]
            };

            db.Books.Add(book);
            await db.SaveChangesAsync();
            return true;
        }
        finally
        {
            Saving = false;
        }
    }
}
