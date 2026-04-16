using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class BookAddViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IBookLookupService lookup)
{
    public BookFormViewModel.BookFormInput BookInput { get; set; } = new();
    public CopyFormViewModel.CopyFormInput CopyInput { get; set; } = new();
    public List<string> LookupCandidates { get; private set; } = [];

    public string? LookupIsbn { get; set; }
    public string? LookupMessage { get; private set; }
    public bool LookingUp { get; private set; }
    public bool Saving { get; private set; }

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

            if (string.IsNullOrWhiteSpace(BookInput.Title)) BookInput.Title = result.Title ?? "";
            if (string.IsNullOrWhiteSpace(BookInput.Subtitle)) BookInput.Subtitle = result.Subtitle;
            if (string.IsNullOrWhiteSpace(BookInput.Author)) BookInput.Author = result.Author ?? "";
            if (string.IsNullOrWhiteSpace(BookInput.DefaultCoverArtUrl)) BookInput.DefaultCoverArtUrl = result.CoverUrl;
            if (string.IsNullOrWhiteSpace(CopyInput.Isbn)) CopyInput.Isbn = result.Isbn;
            if (string.IsNullOrWhiteSpace(CopyInput.Publisher)) CopyInput.Publisher = result.Publisher;
            CopyInput.DatePrinted ??= result.DatePrinted;

            LookupCandidates = result.GenreCandidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            genrePicker.LookupCandidates = LookupCandidates;
            genrePicker.ApplyLookupCandidates(LookupCandidates);

            LookupMessage = $"Prefilled from {result.Source}. Edit anything before saving.";
        }
        finally
        {
            LookingUp = false;
        }
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
            var publisherName = CopyInput.Publisher?.Trim();
            if (!string.IsNullOrEmpty(publisherName))
            {
                publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == publisherName);
                if (publisher is null)
                {
                    publisher = new Publisher { Name = publisherName };
                    db.Publishers.Add(publisher);
                }
            }

            var book = new Book
            {
                Title = BookInput.Title!.Trim(),
                Subtitle = string.IsNullOrWhiteSpace(BookInput.Subtitle) ? null : BookInput.Subtitle.Trim(),
                Author = BookInput.Author!.Trim(),
                Notes = string.IsNullOrWhiteSpace(BookInput.Notes) ? null : BookInput.Notes.Trim(),
                Category = BookInput.Category,
                Status = BookInput.Status,
                Rating = BookInput.Rating,
                DefaultCoverArtUrl = string.IsNullOrWhiteSpace(BookInput.DefaultCoverArtUrl) ? null : BookInput.DefaultCoverArtUrl.Trim(),
                Genres = selectedGenres,
                Copies =
                [
                    new BookCopy
                    {
                        Isbn = CopyInput.Isbn!.Trim(),
                        Format = CopyInput.Format,
                        DatePrinted = CopyInput.DatePrinted,
                        Condition = CopyInput.Condition,
                        Publisher = publisher,
                        CustomCoverArtUrl = string.IsNullOrWhiteSpace(CopyInput.CustomCoverArtUrl) ? null : CopyInput.CustomCoverArtUrl.Trim()
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
