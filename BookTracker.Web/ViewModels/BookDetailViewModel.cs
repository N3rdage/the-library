using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.Services.Covers;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// View model for the book detail page (/books/{id}). The initial load
// projects the Book into a flat display shape (BookDetail record). Inline
// auto-save surfaces for the "browse-and-tweak" fields (rating, status,
// notes, tags) mutate the Current* properties + persist in the same call,
// keeping the page focused on one value at a time. Larger structural
// edits (Work, Edition, Copy) happen in modal dialogs in a later PR.
public class BookDetailViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IBookCoverStorage coverStorage,
    ILogger<BookDetailViewModel> logger)
{
    /// <summary>Server-side cap on user-uploaded cover photos. 10 MB is generous
    /// enough to accept a 12MP phone-camera JPEG without rejection while
    /// bounding the worst-case payload through Blazor Server's SignalR pipe
    /// and the resize step in CoverImageProcessor.</summary>
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    public bool NotFound { get; private set; }
    public BookDetail? Book { get; private set; }

    // Inline-editable state — initialised from Book in InitializeAsync and
    // kept as the source of truth once the page starts mutating. The Book
    // record is the initial snapshot; display binds to Current*.
    public int CurrentRating { get; private set; }
    public BookStatus CurrentStatus { get; private set; }
    public string CurrentNotes { get; set; } = "";
    public List<TagDetail> CurrentTags { get; private set; } = [];

    // Notes-field state: the textbox binds here; a debounced save fires on
    // change + explicit save on blur. The page drives the timing; the VM
    // just persists on demand and tracks saved-ness for the UI indicator.
    public bool NotesDirty { get; private set; }
    public bool NotesSaving { get; private set; }

    public bool IsSingleWork => Book is not null && Book.Works.Count == 1;
    public int TotalEditions => Book?.Editions.Count ?? 0;
    public int TotalCopies => Book?.Editions.Sum(e => e.Copies.Count) ?? 0;

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
                .ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(b => b.Works)
                .ThenInclude(w => w.Genres)
            .Include(b => b.Works)
                .ThenInclude(w => w.Series)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == bookId);

        if (book is null)
        {
            NotFound = true;
            return;
        }

        Book = new BookDetail(
            book.Id,
            book.Title,
            book.Category,
            book.Status,
            book.Rating,
            book.Notes,
            book.DefaultCoverArtUrl,
            book.DateAdded,
            book.Works
                .OrderBy(w => w.SeriesOrder ?? int.MaxValue)
                .ThenBy(w => w.Title)
                .Select(ToWorkDetail)
                .ToList(),
            book.Editions
                .OrderBy(e => e.DatePrinted ?? DateOnly.MaxValue)
                .ThenBy(e => e.Id)
                .Select(ToEditionDetail)
                .ToList(),
            book.Tags
                .OrderBy(t => t.Name)
                .Select(t => new TagDetail(t.Id, t.Name))
                .ToList());

        CurrentRating = book.Rating;
        CurrentStatus = book.Status;
        CurrentNotes = book.Notes ?? "";
        CurrentTags = Book.Tags.ToList();
    }

    public async Task SetRatingAsync(int rating)
    {
        if (Book is null) return;
        if (rating < 0 || rating > 5) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books.FindAsync(Book.Id);
        if (book is null) return;
        book.Rating = rating;
        await db.SaveChangesAsync();
        CurrentRating = rating;
    }

    public async Task SetStatusAsync(BookStatus status)
    {
        if (Book is null) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books.FindAsync(Book.Id);
        if (book is null) return;
        book.Status = status;
        await db.SaveChangesAsync();
        CurrentStatus = status;
    }

    public void MarkNotesDirty() => NotesDirty = true;

    public async Task SaveNotesAsync()
    {
        if (Book is null || !NotesDirty) return;

        NotesSaving = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var book = await db.Books.FindAsync(Book.Id);
            if (book is null) return;
            book.Notes = string.IsNullOrWhiteSpace(CurrentNotes) ? null : CurrentNotes.Trim();
            await db.SaveChangesAsync();
            NotesDirty = false;
        }
        finally
        {
            NotesSaving = false;
        }
    }

    /// <summary>Returns the added (or existing, re-attached) tag. Null if the name was blank.</summary>
    public async Task<TagDetail?> AddTagAsync(string name)
    {
        if (Book is null || string.IsNullOrWhiteSpace(name)) return null;

        var normalized = name.Trim().ToLowerInvariant();
        if (CurrentTags.Any(t => t.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books.Include(b => b.Tags).FirstOrDefaultAsync(b => b.Id == Book.Id);
        if (book is null) return null;

        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == normalized);
        if (tag is null)
        {
            tag = new Tag { Name = normalized };
            db.Tags.Add(tag);
        }

        if (book.Tags.All(t => t.Id != tag.Id || tag.Id == 0))
        {
            book.Tags.Add(tag);
        }

        await db.SaveChangesAsync();

        var detail = new TagDetail(tag.Id, tag.Name);
        CurrentTags.Add(detail);
        CurrentTags = CurrentTags.OrderBy(t => t.Name).ToList();
        return detail;
    }

    /// <summary>
    /// Deletes a Copy. If it was the last Copy on its Edition, the Edition
    /// is removed too (matches the existing Edit-page behaviour — an
    /// Edition with no Copies doesn't represent anything useful).
    /// </summary>
    public async Task DeleteCopyAsync(int copyId)
    {
        if (Book is null) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var copy = await db.Copies.Include(c => c.Edition).ThenInclude(e => e.Copies).FirstOrDefaultAsync(c => c.Id == copyId);
        if (copy is null) return;

        var edition = copy.Edition;
        db.Copies.Remove(copy);
        if (edition.Copies.Count <= 1)
        {
            db.Editions.Remove(edition);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Uploads a user-supplied photo as the cover for a specific Edition,
    /// replacing any existing cover. Used for rare / old editions where no
    /// online cover is available. Returns a <see cref="UploadCoverResult"/>
    /// with success/error shape so the page can render the right snackbar.
    /// </summary>
    public async Task<UploadCoverResult> UploadEditionCoverAsync(int editionId, IBrowserFile file, CancellationToken ct)
    {
        if (Book is null) return UploadCoverResult.Failure("Book not loaded.");

        if (file.Size > MaxUploadBytes)
        {
            return UploadCoverResult.Failure($"File too large ({file.Size / 1024 / 1024} MB). Max is {MaxUploadBytes / 1024 / 1024} MB.");
        }

        if (!coverStorage.IsEnabled)
        {
            return UploadCoverResult.Failure("Cover storage is not configured.");
        }

        byte[] bytes;
        try
        {
            using var stream = file.OpenReadStream(MaxUploadBytes, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read uploaded cover stream for Edition {EditionId}.", editionId);
            return UploadCoverResult.Failure("Couldn't read the uploaded file.");
        }

        string newUrl;
        try
        {
            newUrl = await coverStorage.UploadAsync(bytes, file.ContentType, $"editions/{editionId}", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cover upload failed for Edition {EditionId}.", editionId);
            return UploadCoverResult.Failure("Upload failed. Try again or check the file.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var edition = await db.Editions.FirstOrDefaultAsync(e => e.Id == editionId, ct);
        if (edition is null) return UploadCoverResult.Failure("Edition not found.");

        edition.CoverUrl = newUrl;
        edition.IsUserSupplied = true;
        await db.SaveChangesAsync(ct);

        // Refresh the in-memory snapshot so the page re-renders against the new URL.
        await InitializeAsync(Book.Id);

        return UploadCoverResult.Ok(newUrl);
    }

    public record UploadCoverResult(bool Success, string? ErrorMessage, string? NewUrl)
    {
        public static UploadCoverResult Ok(string url) => new(true, null, url);
        public static UploadCoverResult Failure(string error) => new(false, error, null);
    }

    public async Task RemoveTagAsync(int tagId)
    {
        if (Book is null) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books.Include(b => b.Tags).FirstOrDefaultAsync(b => b.Id == Book.Id);
        if (book is null) return;

        var tag = book.Tags.FirstOrDefault(t => t.Id == tagId);
        if (tag is not null)
        {
            book.Tags.Remove(tag);
            await db.SaveChangesAsync();
        }

        CurrentTags.RemoveAll(t => t.Id == tagId);
    }

    /// <summary>Tag autocomplete — returns existing tags not already assigned, filtered by substring.</summary>
    public async Task<IEnumerable<string>> SearchTagsAsync(string query, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var assigned = CurrentTags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var q = (query ?? "").Trim().ToLowerInvariant();

        var names = await db.Tags
            .Where(t => string.IsNullOrEmpty(q) || t.Name.Contains(q))
            .OrderBy(t => t.Name)
            .Select(t => t.Name)
            .Take(20)
            .ToListAsync(ct);

        return names.Where(n => !assigned.Contains(n));
    }

    private static WorkDetail ToWorkDetail(Work w) => new(
        w.Id,
        w.Title,
        w.Subtitle,
        // AuthorName carries the formatted multi-author display ("Preston & Child")
        // post-PR2; the field name stays so Razor consumers don't have to change.
        WorkAuthorshipFormatter.Display(w),
        // LeadAuthorId is the first WorkAuthor entry's Author — used by the
        // BookDetail '+author' link to deep-link into the /authors page.
        w.WorkAuthors.OrderBy(wa => wa.Order).Select(wa => wa.AuthorId).FirstOrDefault(),
        PartialDateParser.Format(w.FirstPublishedDate, w.FirstPublishedDatePrecision),
        w.Genres.OrderBy(g => g.Name).Select(g => g.Name).ToList(),
        w.Series is null ? null : new SeriesInfo(w.Series.Id, w.Series.Name, w.Series.Type, w.SeriesOrder));

    private static EditionDetail ToEditionDetail(Edition e) => new(
        e.Id,
        e.Isbn,
        e.Format,
        e.Format.DisplayName(),
        e.Publisher?.Name,
        PartialDateParser.Format(e.DatePrinted, e.DatePrintedPrecision),
        e.CoverUrl,
        e.IsUserSupplied,
        e.Copies
            .OrderBy(c => c.DateAcquired ?? DateTime.MaxValue)
            .ThenBy(c => c.Id)
            .Select(c => new CopyDetail(c.Id, c.Condition, c.DateAcquired, c.Notes))
            .ToList());

    public record BookDetail(
        int Id,
        string Title,
        BookCategory Category,
        BookStatus Status,
        int Rating,
        string? Notes,
        string? CoverUrl,
        DateTime DateAdded,
        IReadOnlyList<WorkDetail> Works,
        IReadOnlyList<EditionDetail> Editions,
        IReadOnlyList<TagDetail> Tags);

    public record WorkDetail(
        int Id,
        string Title,
        string? Subtitle,
        /// <summary>Formatted display string ("Preston" / "Preston &amp; Child" / "Preston, Child, Pendergast").</summary>
        string AuthorName,
        /// <summary>The lead author'\''s Id — used for /authors/{id} deep-links from the BookDetail page.</summary>
        int LeadAuthorId,
        string FirstPublishedDisplay,
        IReadOnlyList<string> Genres,
        SeriesInfo? Series);

    public record SeriesInfo(int Id, string Name, SeriesType Type, int? Order);

    public record EditionDetail(
        int Id,
        string? Isbn,
        BookFormat Format,
        string FormatDisplay,
        string? Publisher,
        string DatePrintedDisplay,
        string? CoverUrl,
        bool IsUserSupplied,
        IReadOnlyList<CopyDetail> Copies);

    public record CopyDetail(int Id, BookCondition Condition, DateTime? DateAcquired, string? Notes);

    public record TagDetail(int Id, string Name);
}
