using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class BookEditViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public BookFormViewModel.BookFormInput? BookInput { get; private set; }

    // The "primary" Work — first in the Book's Works list. Editing happens
    // here. For single-Work books (the common case) this is the only Work.
    public WorkFormViewModel.WorkFormInput? PrimaryWorkInput { get; private set; }
    public int? PrimaryWorkId { get; private set; }
    public List<int> SelectedGenreIds { get; set; } = [];
    public int? SelectedSeriesId { get; set; }
    public int? SeriesOrder { get; set; }

    // Compendium extras — Works beyond the first. Read-only display + add/
    // remove only in PR 2; full inline editing of extras is tracked in
    // TODO.md.
    public List<WorkSummary> OtherWorks { get; private set; } = [];
    public string? NewWorkTitle { get; set; }
    public string? NewWorkAuthor { get; set; }

    public bool NotFound { get; private set; }
    public bool Saving { get; private set; }
    public string? SuccessMessage { get; set; }

    // Tags
    public List<TagItem> AssignedTags { get; private set; } = [];
    public List<TagItem> AvailableTags { get; private set; } = [];
    public string NewTagName { get; set; } = "";

    // Editions & Copies
    public List<EditionCopyRow> EditionCopies { get; private set; } = [];
    public bool ShowingNewEdition { get; set; }
    public EditionFormViewModel.EditionFormInput NewEditionInput { get; set; } = new();
    public CopyFormViewModel.CopyFormInput NewCopyInput { get; set; } = new();

    // Inline edition/copy editing
    public int? EditingCopyId { get; set; }
    public CopyEditInput EditCopyInput { get; set; } = new();
    public int? ConfirmingDeleteCopyId { get; set; }

    public List<SeriesOption> AvailableSeries { get; private set; } = [];

    // Book deletion
    public bool ConfirmingDeleteBook { get; set; }
    public bool Deleting { get; private set; }

    public async Task InitializeAsync(int bookId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var book = await db.Books
            .Include(b => b.Tags)
            .Include(b => b.Editions)
                .ThenInclude(e => e.Copies)
            .Include(b => b.Editions)
                .ThenInclude(e => e.Publisher)
            .Include(b => b.Works)
                .ThenInclude(w => w.Genres)
            .Include(b => b.Works)
                .ThenInclude(w => w.Author)
            .FirstOrDefaultAsync(b => b.Id == bookId);

        if (book is null)
        {
            NotFound = true;
            return;
        }

        BookInput = new BookFormViewModel.BookFormInput
        {
            Title = book.Title,
            Category = book.Category,
            Status = book.Status,
            Rating = book.Rating,
            Notes = book.Notes,
            DefaultCoverArtUrl = book.DefaultCoverArtUrl
        };

        var primary = book.Works.FirstOrDefault();
        if (primary is not null)
        {
            PrimaryWorkId = primary.Id;
            PrimaryWorkInput = new WorkFormViewModel.WorkFormInput
            {
                Title = primary.Title,
                Subtitle = primary.Subtitle,
                Author = primary.Author.Name,
                FirstPublishedDate = primary.FirstPublishedDate,
            };
            SelectedGenreIds = primary.Genres.Select(g => g.Id).ToList();
            SelectedSeriesId = primary.SeriesId;
            SeriesOrder = primary.SeriesOrder;
        }
        else
        {
            // Defensive: shouldn't happen post-cutover (every Book has a Work).
            PrimaryWorkInput = new WorkFormViewModel.WorkFormInput { Title = book.Title };
        }

        OtherWorks = book.Works.Skip(1)
            .Select(w => new WorkSummary(w.Id, w.Title, w.Author.Name, w.Genres.Count))
            .ToList();

        AssignedTags = book.Tags.Select(t => new TagItem(t.Id, t.Name)).ToList();
        EditionCopies = book.Editions
            .SelectMany(e => e.Copies.Select(c => new EditionCopyRow(
                e.Id, c.Id, e.Isbn, e.Format, c.Condition,
                e.Publisher?.Name, e.DatePrinted, e.CoverUrl,
                c.Notes, c.DateAcquired)))
            .ToList();

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

    public void ShowAddEdition()
    {
        NewEditionInput = new EditionFormViewModel.EditionFormInput();
        NewCopyInput = new CopyFormViewModel.CopyFormInput();
        ShowingNewEdition = true;
    }

    public async Task SaveNewEditionAsync(int bookId)
    {
        if (string.IsNullOrWhiteSpace(NewEditionInput.Isbn)) return;

        await using var db = await dbFactory.CreateDbContextAsync();

        Publisher? publisher = null;
        var pubName = NewEditionInput.Publisher?.Trim();
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
            Isbn = NewEditionInput.Isbn.Trim(),
            Format = NewEditionInput.Format,
            DatePrinted = NewEditionInput.DatePrinted,
            Publisher = publisher,
            CoverUrl = string.IsNullOrWhiteSpace(NewEditionInput.CoverUrl) ? null : NewEditionInput.CoverUrl.Trim(),
            Copies = [new Copy { Condition = NewCopyInput.Condition }]
        };

        db.Editions.Add(edition);
        await db.SaveChangesAsync();

        var copy = edition.Copies[0];
        EditionCopies.Add(new EditionCopyRow(
            edition.Id, copy.Id, edition.Isbn, edition.Format, copy.Condition,
            publisher?.Name, edition.DatePrinted, edition.CoverUrl, copy.Notes, copy.DateAcquired));
        ShowingNewEdition = false;
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

    public async Task AddOtherWorkAsync(int bookId)
    {
        if (string.IsNullOrWhiteSpace(NewWorkTitle) || string.IsNullOrWhiteSpace(NewWorkAuthor)) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books.Include(b => b.Works).FirstOrDefaultAsync(b => b.Id == bookId);
        if (book is null) return;

        var author = await AuthorResolver.FindOrCreateAsync(NewWorkAuthor, db);
        var work = new Work
        {
            Title = NewWorkTitle.Trim(),
            Author = author,
        };
        book.Works.Add(work);
        await db.SaveChangesAsync();

        OtherWorks.Add(new WorkSummary(work.Id, work.Title, author.Name, 0));
        NewWorkTitle = null;
        NewWorkAuthor = null;
    }

    public async Task RemoveOtherWorkAsync(int workId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var work = await db.Works.Include(w => w.Books).FirstOrDefaultAsync(w => w.Id == workId);
        if (work is null) return;

        // If this Work is only attached to this Book, delete it; otherwise
        // just unlink from this Book (it lives on in another compendium).
        if (work.Books.Count <= 1)
        {
            db.Works.Remove(work);
        }
        else
        {
            // Find which Book to detach from — the one we're editing (the
            // sole Book in OtherWorks scope is whichever the page is for).
            // OtherWorks is loaded for one Book, so this is safe.
            var attachedBook = work.Books.First();
            attachedBook.Works.Remove(work);
        }
        await db.SaveChangesAsync();

        OtherWorks.RemoveAll(w => w.Id == workId);
    }

    public async Task SaveAsync(int bookId)
    {
        if (BookInput is null || PrimaryWorkInput is null) return;

        Saving = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var book = await db.Books
                .Include(b => b.Tags)
                .Include(b => b.Works).ThenInclude(w => w.Genres)
                .Include(b => b.Works).ThenInclude(w => w.Author)
                .FirstOrDefaultAsync(b => b.Id == bookId);

            if (book is null) { NotFound = true; return; }

            book.Title = BookInput.Title!.Trim();
            book.Category = BookInput.Category;
            book.Status = BookInput.Status;
            book.Rating = BookInput.Rating;
            book.Notes = string.IsNullOrWhiteSpace(BookInput.Notes) ? null : BookInput.Notes.Trim();
            book.DefaultCoverArtUrl = string.IsNullOrWhiteSpace(BookInput.DefaultCoverArtUrl) ? null : BookInput.DefaultCoverArtUrl.Trim();

            var selectedTags = await db.Tags
                .Where(t => AssignedTags.Select(at => at.Id).Contains(t.Id))
                .ToListAsync();
            book.Tags.Clear();
            book.Tags.AddRange(selectedTags);

            // Update the primary Work in place. PrimaryWorkId is set in
            // InitializeAsync; falling back to the first Work in the book
            // covers the defensive case where it wasn't set.
            var primary = (PrimaryWorkId is int id ? book.Works.FirstOrDefault(w => w.Id == id) : null)
                          ?? book.Works.FirstOrDefault();

            if (primary is not null)
            {
                primary.Title = PrimaryWorkInput.Title!.Trim();
                primary.Subtitle = string.IsNullOrWhiteSpace(PrimaryWorkInput.Subtitle) ? null : PrimaryWorkInput.Subtitle.Trim();
                primary.Author = await AuthorResolver.FindOrCreateAsync(PrimaryWorkInput.Author!, db);
                primary.FirstPublishedDate = PrimaryWorkInput.FirstPublishedDate;
                primary.SeriesId = SelectedSeriesId;
                primary.SeriesOrder = SelectedSeriesId.HasValue ? SeriesOrder : null;

                var selectedGenres = await db.Genres
                    .Where(g => SelectedGenreIds.Contains(g.Id))
                    .ToListAsync();
                primary.Genres.Clear();
                foreach (var g in selectedGenres) primary.Genres.Add(g);
            }

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
    public record EditionCopyRow(int EditionId, int CopyId, string? Isbn, BookFormat Format, BookCondition Condition, string? PublisherName, DateOnly? DatePrinted, string? CoverUrl, string? CopyNotes, DateTime? DateAcquired);
    public record WorkSummary(int Id, string Title, string Author, int GenreCount);

    public class CopyEditInput
    {
        public string? Isbn { get; set; }
        public BookFormat Format { get; set; }
        public BookCondition Condition { get; set; }
        public string? Publisher { get; set; }
        public DateOnly? DatePrinted { get; set; }
    }
}
