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

    // Copies
    public List<CopyRow> Copies { get; private set; } = [];
    public bool ShowingNewCopy { get; set; }
    public CopyFormViewModel.CopyFormInput NewCopyInput { get; set; } = new();

    // Inline copy editing
    public int? EditingCopyId { get; set; }
    public CopyEditInput EditCopyInput { get; set; } = new();
    public int? ConfirmingDeleteCopyId { get; set; }

    // Book deletion
    public bool ConfirmingDeleteBook { get; set; }
    public bool Deleting { get; private set; }

    public async Task InitializeAsync(int bookId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var book = await db.Books
            .Include(b => b.Genres)
            .Include(b => b.Tags)
            .Include(b => b.Copies)
                .ThenInclude(c => c.Publisher)
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
        Copies = book.Copies.Select(c => new CopyRow(c.Id, c.Isbn, c.Format, c.Condition, c.Publisher?.Name, c.DatePrinted, c.CustomCoverArtUrl)).ToList();

        await LoadAvailableTagsAsync();
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

        var copy = new BookCopy
        {
            BookId = bookId,
            Isbn = NewCopyInput.Isbn.Trim(),
            Format = NewCopyInput.Format,
            DatePrinted = NewCopyInput.DatePrinted,
            Condition = NewCopyInput.Condition,
            Publisher = publisher,
            CustomCoverArtUrl = string.IsNullOrWhiteSpace(NewCopyInput.CustomCoverArtUrl) ? null : NewCopyInput.CustomCoverArtUrl.Trim()
        };

        db.BookCopies.Add(copy);
        await db.SaveChangesAsync();

        Copies.Add(new CopyRow(copy.Id, copy.Isbn, copy.Format, copy.Condition, publisher?.Name, copy.DatePrinted, copy.CustomCoverArtUrl));
        ShowingNewCopy = false;
    }

    public void StartEditCopy(CopyRow copy)
    {
        EditingCopyId = copy.Id;
        EditCopyInput = new CopyEditInput
        {
            Isbn = copy.Isbn,
            Format = copy.Format,
            Condition = copy.Condition,
            Publisher = copy.PublisherName,
            DatePrinted = copy.DatePrinted
        };
    }

    public void CancelCopyEdit() => EditingCopyId = null;

    public async Task SaveCopyEditAsync()
    {
        if (EditingCopyId is not int copyId) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var copy = await db.BookCopies.Include(c => c.Publisher).FirstOrDefaultAsync(c => c.Id == copyId);
        if (copy is null) { EditingCopyId = null; return; }

        copy.Isbn = EditCopyInput.Isbn?.Trim() ?? copy.Isbn;
        copy.Format = EditCopyInput.Format;
        copy.Condition = EditCopyInput.Condition;
        copy.DatePrinted = EditCopyInput.DatePrinted;

        var pubName = EditCopyInput.Publisher?.Trim();
        if (!string.IsNullOrEmpty(pubName))
        {
            var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);
            if (publisher is null)
            {
                publisher = new Publisher { Name = pubName };
                db.Publishers.Add(publisher);
            }
            copy.Publisher = publisher;
        }
        else
        {
            copy.Publisher = null;
            copy.PublisherId = null;
        }

        await db.SaveChangesAsync();

        var idx = Copies.FindIndex(c => c.Id == copyId);
        if (idx >= 0)
        {
            Copies[idx] = new CopyRow(copy.Id, copy.Isbn, copy.Format, copy.Condition, pubName, copy.DatePrinted, copy.CustomCoverArtUrl);
        }
        EditingCopyId = null;
    }

    public async Task DeleteCopyAsync(CopyRow copy)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.BookCopies.FindAsync(copy.Id);
        if (entity is not null)
        {
            db.BookCopies.Remove(entity);
            await db.SaveChangesAsync();
        }
        Copies.RemoveAll(c => c.Id == copy.Id);
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

            await db.SaveChangesAsync();
            SuccessMessage = "Book saved successfully.";
        }
        finally
        {
            Saving = false;
        }
    }

    public record TagItem(int Id, string Name);
    public record CopyRow(int Id, string Isbn, BookFormat Format, BookCondition Condition, string? PublisherName, DateOnly? DatePrinted, string? CustomCoverArtUrl);

    public class CopyEditInput
    {
        public string? Isbn { get; set; }
        public BookFormat Format { get; set; }
        public BookCondition Condition { get; set; }
        public string? Publisher { get; set; }
        public DateOnly? DatePrinted { get; set; }
    }
}
