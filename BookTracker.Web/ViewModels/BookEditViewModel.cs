using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class BookEditViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public BookFormViewModel.BookFormInput? BookInput { get; private set; }
    public List<int> SelectedGenreIds { get; set; } = [];
    public bool NotFound { get; private set; }
    public bool Saving { get; private set; }
    public string? SuccessMessage { get; set; }

    // Tags
    public List<TagItem> AssignedTags { get; private set; } = [];
    public List<TagItem> AvailableTags { get; private set; } = [];
    public string NewTagName { get; set; } = "";

    // Editions & Copies
    public List<EditionCopyRow> EditionCopies { get; private set; } = [];
    public bool ShowingNewCopy { get; set; }
    public CopyFormViewModel.CopyFormInput NewCopyInput { get; set; } = new();

    // Inline edition/copy editing
    public int? EditingCopyId { get; set; }
    public CopyEditInput EditCopyInput { get; set; } = new();
    public int? ConfirmingDeleteCopyId { get; set; }

    // Series
    public int? SelectedSeriesId { get; set; }
    public int? SeriesOrder { get; set; }
    public List<SeriesOption> AvailableSeries { get; private set; } = [];

    // Book deletion
    public bool ConfirmingDeleteBook { get; set; }
    public bool Deleting { get; private set; }

    public async Task InitializeAsync(int bookId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var book = await db.Books
            .Include(b => b.Genres)
            .Include(b => b.Tags)
            .Include(b => b.Editions)
                .ThenInclude(e => e.Copies)
            .Include(b => b.Editions)
                .ThenInclude(e => e.Publisher)
            .FirstOrDefaultAsync(b => b.Id == bookId);

        if (book is null)
        {
            NotFound = true;
            return;
        }

        BookInput = new BookFormViewModel.BookFormInput
        {
            Title = book.Title,
            Subtitle = book.Subtitle,
            Author = book.Author,
            Category = book.Category,
            Status = book.Status,
            Rating = book.Rating,
            Notes = book.Notes,
            DefaultCoverArtUrl = book.DefaultCoverArtUrl
        };

        SelectedGenreIds = book.Genres.Select(g => g.Id).ToList();
        AssignedTags = book.Tags.Select(t => new TagItem(t.Id, t.Name)).ToList();
        EditionCopies = book.Editions
            .SelectMany(e => e.Copies.Select(c => new EditionCopyRow(
                e.Id, c.Id, e.Isbn, e.Format, c.Condition,
                e.Publisher?.Name, e.DatePrinted, e.CoverUrl,
                c.Notes, c.DateAcquired)))
            .ToList();

        SelectedSeriesId = book.SeriesId;
        SeriesOrder = book.SeriesOrder;

        await LoadAvailableTagsAsync();
        await LoadAvailableSeriesAsync();
    }

    public async Task LoadAvailableSeriesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        AvailableSeries = await db.Series
            .OrderBy(s => s.Name)
            .Select(s => new SeriesOption(s.Id, s.Name, s.Type))
            .ToListAsync();
    }

    public async Task LoadAvailableTagsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var assignedIds = AssignedTags.Select(t => t.Id).ToHashSet();
        AvailableTags = await db.Tags
            .Where(t => !assignedIds.Contains(t.Id))
            .OrderBy(t => t.Name)
            .Select(t => new TagItem(t.Id, t.Name))
            .ToListAsync();
    }

    public async Task AddTagAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTagName)) return;
        var name = NewTagName.Trim().ToLowerInvariant();

        await using var db = await dbFactory.CreateDbContextAsync();
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name);
        if (tag is null)
        {
            tag = new Tag { Name = name };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
        }

        if (AssignedTags.All(t => t.Id != tag.Id))
        {
            AssignedTags.Add(new TagItem(tag.Id, tag.Name));
            await LoadAvailableTagsAsync();
        }

        NewTagName = "";
    }

    public void RemoveTag(TagItem tag)
    {
        AssignedTags.RemoveAll(t => t.Id == tag.Id);
        if (AvailableTags.All(t => t.Id != tag.Id))
        {
            AvailableTags.Add(tag);
            AvailableTags = AvailableTags.OrderBy(t => t.Name).ToList();
        }
    }

    public void ShowAddCopy()
    {
        NewCopyInput = new CopyFormViewModel.CopyFormInput();
        ShowingNewCopy = true;
    }

    public async Task SaveNewCopyAsync(int bookId)
    {
        if (string.IsNullOrWhiteSpace(NewCopyInput.Isbn)) return;

        await using var db = await dbFactory.CreateDbContextAsync();

        Publisher? publisher = null;
        var pubName = NewCopyInput.Publisher?.Trim();
        if (!string.IsNullOrEmpty(pubName))
        {
            publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);
            if (publisher is null)
            {
                publisher = new Publisher { Name = pubName };
                db.Publishers.Add(publisher);
            }
        }

        var edition = new Edition
        {
            BookId = bookId,
            Isbn = NewCopyInput.Isbn.Trim(),
            Format = NewCopyInput.Format,
            DatePrinted = NewCopyInput.DatePrinted,
            Publisher = publisher,
            CoverUrl = string.IsNullOrWhiteSpace(NewCopyInput.CustomCoverArtUrl) ? null : NewCopyInput.CustomCoverArtUrl.Trim(),
            Copies = [new Copy { Condition = NewCopyInput.Condition }]
        };

        db.Editions.Add(edition);
        await db.SaveChangesAsync();

        var copy = edition.Copies[0];
        EditionCopies.Add(new EditionCopyRow(
            edition.Id, copy.Id, edition.Isbn, edition.Format, copy.Condition,
            publisher?.Name, edition.DatePrinted, edition.CoverUrl, copy.Notes, copy.DateAcquired));
        ShowingNewCopy = false;
    }

    public void StartEditCopy(EditionCopyRow row)
    {
        EditingCopyId = row.CopyId;
        EditCopyInput = new CopyEditInput
        {
            Isbn = row.Isbn,
            Format = row.Format,
            Condition = row.Condition,
            Publisher = row.PublisherName,
            DatePrinted = row.DatePrinted
        };
    }

    public void CancelCopyEdit() => EditingCopyId = null;

    public async Task SaveCopyEditAsync()
    {
        if (EditingCopyId is not int copyId) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var copy = await db.Copies.Include(c => c.Edition).ThenInclude(e => e.Publisher).FirstOrDefaultAsync(c => c.Id == copyId);
        if (copy is null) { EditingCopyId = null; return; }

        var edition = copy.Edition;
        edition.Isbn = EditCopyInput.Isbn?.Trim() ?? edition.Isbn;
        edition.Format = EditCopyInput.Format;
        edition.DatePrinted = EditCopyInput.DatePrinted;
        copy.Condition = EditCopyInput.Condition;

        var pubName = EditCopyInput.Publisher?.Trim();
        if (!string.IsNullOrEmpty(pubName))
        {
            var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);
            if (publisher is null)
            {
                publisher = new Publisher { Name = pubName };
                db.Publishers.Add(publisher);
            }
            edition.Publisher = publisher;
        }
        else
        {
            edition.Publisher = null;
            edition.PublisherId = null;
        }

        await db.SaveChangesAsync();

        var idx = EditionCopies.FindIndex(c => c.CopyId == copyId);
        if (idx >= 0)
        {
            EditionCopies[idx] = new EditionCopyRow(
                edition.Id, copy.Id, edition.Isbn, edition.Format, copy.Condition,
                pubName, edition.DatePrinted, edition.CoverUrl, copy.Notes, copy.DateAcquired);
        }
        EditingCopyId = null;
    }

    public async Task DeleteCopyAsync(EditionCopyRow row)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var copy = await db.Copies.Include(c => c.Edition).ThenInclude(e => e.Copies).FirstOrDefaultAsync(c => c.Id == row.CopyId);
        if (copy is not null)
        {
            var edition = copy.Edition;
            db.Copies.Remove(copy);

            // If this was the last copy on the edition, remove the edition too
            if (edition.Copies.Count <= 1)
            {
                db.Editions.Remove(edition);
            }

            await db.SaveChangesAsync();
        }
        EditionCopies.RemoveAll(c => c.CopyId == row.CopyId);
        ConfirmingDeleteCopyId = null;
    }

    /// <returns>true if the book was deleted (caller should navigate away)</returns>
    public async Task<bool> DeleteBookAsync(int bookId)
    {
        Deleting = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var book = await db.Books.FindAsync(bookId);
            if (book is not null)
            {
                db.Books.Remove(book);
                await db.SaveChangesAsync();
            }
            return true;
        }
        finally
        {
            Deleting = false;
        }
    }

    public async Task SaveAsync(int bookId)
    {
        Saving = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var book = await db.Books
                .Include(b => b.Genres)
                .Include(b => b.Tags)
                .FirstOrDefaultAsync(b => b.Id == bookId);

            if (book is null) { NotFound = true; return; }

            book.Title = BookInput!.Title!.Trim();
            book.Subtitle = string.IsNullOrWhiteSpace(BookInput.Subtitle) ? null : BookInput.Subtitle.Trim();
            book.Author = BookInput.Author!.Trim();
            book.Category = BookInput.Category;
            book.Status = BookInput.Status;
            book.Rating = BookInput.Rating;
            book.Notes = string.IsNullOrWhiteSpace(BookInput.Notes) ? null : BookInput.Notes.Trim();
            book.DefaultCoverArtUrl = string.IsNullOrWhiteSpace(BookInput.DefaultCoverArtUrl) ? null : BookInput.DefaultCoverArtUrl.Trim();

            var selectedGenres = await db.Genres
                .Where(g => SelectedGenreIds.Contains(g.Id))
                .ToListAsync();
            book.Genres.Clear();
            book.Genres.AddRange(selectedGenres);

            var selectedTags = await db.Tags
                .Where(t => AssignedTags.Select(at => at.Id).Contains(t.Id))
                .ToListAsync();
            book.Tags.Clear();
            book.Tags.AddRange(selectedTags);

            book.SeriesId = SelectedSeriesId;
            book.SeriesOrder = SelectedSeriesId.HasValue ? SeriesOrder : null;

            await db.SaveChangesAsync();
            SuccessMessage = "Book saved successfully.";
        }
        finally
        {
            Saving = false;
        }
    }

    public record SeriesOption(int Id, string Name, SeriesType Type);
    public record TagItem(int Id, string Name);
    public record EditionCopyRow(int EditionId, int CopyId, string Isbn, BookFormat Format, BookCondition Condition, string? PublisherName, DateOnly? DatePrinted, string? CoverUrl, string? CopyNotes, DateTime? DateAcquired);

    public class CopyEditInput
    {
        public string? Isbn { get; set; }
        public BookFormat Format { get; set; }
        public BookCondition Condition { get; set; }
        public string? Publisher { get; set; }
        public DateOnly? DatePrinted { get; set; }
    }
}
