using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Works;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.Services.Covers;
using Microsoft.AspNetCore.Components.Forms;
using BookTracker.Application.Formatting;

namespace BookTracker.Web.ViewModels;

// View model for the book detail page (/books/{id}). The initial load
// projects the Book into a flat display shape (BookDetail record). Inline
// auto-save surfaces for the "browse-and-tweak" fields (rating, status,
// notes, tags) mutate the Current* properties + persist in the same call,
// keeping the page focused on one value at a time. Larger structural
// edits (Work, Edition, Copy) happen in modal dialogs in a later PR.
// As of PR6b-3 the VM is fully off DbContext: every read (InitializeAsync ->
// GetBookDetail, the tag autocomplete -> GetTagSuggestions) and every write
// (rating/status/notes/works/copies/cover, and now tags -> AddTagToBook /
// RemoveTagFromBook) routes through BookTracker.Application via IDispatcher.
// The VM keeps only presentation state (the Current* inline-edit fields and
// the loaded BookDetail snapshot).
public class BookDetailViewModel(
    IBookCoverStorage coverStorage,
    IWorkSearchService workSearch,
    ILogger<BookDetailViewModel> logger,
    IDispatcher dispatcher)
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
        var book = await dispatcher.Query(new GetBookDetail(bookId));
        if (book is null)
        {
            NotFound = true;
            return;
        }

        Book = book;
        CurrentRating = book.Rating;
        CurrentStatus = book.Status;
        CurrentNotes = book.Notes ?? "";
        CurrentTags = Book.Tags.ToList();
    }

    public async Task SetRatingAsync(int rating)
    {
        if (Book is null) return;
        await dispatcher.Send(new RateBook(Book.Id, rating));
        CurrentRating = rating;
    }

    public async Task SetStatusAsync(BookStatus status)
    {
        if (Book is null) return;
        await dispatcher.Send(new SetBookStatus(Book.Id, status));
        CurrentStatus = status;
    }

    public void MarkNotesDirty() => NotesDirty = true;

    public async Task SaveNotesAsync()
    {
        if (Book is null || !NotesDirty) return;

        NotesSaving = true;
        try
        {
            await dispatcher.Send(new UpdateBookNotes(Book.Id, CurrentNotes));
            NotesDirty = false;
        }
        finally
        {
            NotesSaving = false;
        }
    }

    /// <summary>Returns the added (or existing, re-attached) tag. Null if the name was blank or already on the book.</summary>
    public async Task<TagDetail?> AddTagAsync(string name)
    {
        if (Book is null || string.IsNullOrWhiteSpace(name)) return null;

        // Normalise here too: the dedup guard + the optimistic CurrentTags entry
        // must use the same rule the handler's TagResolver stores by.
        var normalized = name.Trim().ToLowerInvariant();
        if (CurrentTags.Any(t => t.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var tagId = await dispatcher.Send(new AddTagToBook(Book.Id, name));
        if (tagId is null) return null;

        var detail = new TagDetail(tagId.Value, normalized);
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
        await dispatcher.Send(new DeleteCopy(Book.Id, copyId));
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

        try
        {
            await dispatcher.Send(new SetEditionCover(editionId, newUrl, IsUserSupplied: true), ct);
        }
        catch (NotFoundException)
        {
            return UploadCoverResult.Failure("Edition not found.");
        }

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

        await dispatcher.Send(new RemoveTagFromBook(Book.Id, tagId));
        CurrentTags.RemoveAll(t => t.Id == tagId);
    }

    /// <summary>Search Works to attach to this Book — used by the
    /// AddExistingWorkDialog. Already-attached Works are filtered out
    /// server-side so re-attach attempts can't reach the dialog.</summary>
    public Task<IReadOnlyList<WorkSearchResult>> SearchAttachableWorksAsync(string query, CancellationToken ct)
    {
        if (Book is null) return Task.FromResult<IReadOnlyList<WorkSearchResult>>([]);
        return workSearch.SearchAsync(query, excludeBookId: Book.Id, ct: ct);
    }

    /// <summary>Attach an existing Work to this Book. Returns the Work's
    /// title on success, null when the attach was rejected (Book missing,
    /// Work missing, or already attached — the search filter prevents
    /// this last case in practice but the guard is defensive against
    /// stale dialog state). Refreshes the snapshot so the page re-renders
    /// with the new Work in the Works list.</summary>
    public async Task<string?> AttachExistingWorkAsync(int workId)
    {
        if (Book is null) return null;
        var title = await dispatcher.Send(new AttachWorkToBook(Book.Id, workId));
        if (title is not null) await InitializeAsync(Book.Id);
        return title;
    }

    /// <summary>Create a new Work (with the supplied fields) and attach it
    /// to this Book — used by AddWorkDialog when the user typed a title
    /// that didn't match any existing Work and filled out the form. Returns
    /// the new Work id, or null when the input is invalid (no Book,
    /// missing title, no contributors of any role). Refreshes the
    /// snapshot so the page shows the new Work without a hard reload.</summary>
    public async Task<int?> CreateAndAttachWorkAsync(
        string title,
        IReadOnlyList<string> authorNames,
        string? subtitle,
        string? firstPublishedDate,
        IReadOnlyList<int> genreIds,
        IReadOnlyList<ContributorEntry>? contributors = null)
    {
        if (Book is null) return null;
        if (string.IsNullOrWhiteSpace(title)) return null;

        var firstPub = PartialDateParser.TryParse(firstPublishedDate) ?? PartialDate.Empty;
        var contributorInputs = (contributors ?? [])
            .Select(c => new ContributorInput(c.Name, c.Role))
            .ToList();

        var id = await dispatcher.Send(new CreateWorkOnBook(
            Book.Id, title, subtitle, authorNames, contributorInputs,
            firstPub.Date, firstPub.Precision, genreIds));
        if (id is not null) await InitializeAsync(Book.Id);
        return id;
    }

    /// <summary>Attach a batch of new + existing Works to this Book in
    /// one transaction. Mirrors <see cref="BookAddViewModel.SaveAsync"/>'s
    /// collection-mode branch (single-name-resolution pass, attach-by-id
    /// for existing rows, build-from-fields for new rows) but the Book
    /// already exists. Used by AddMultipleWorksDialog on the View page
    /// to escape the single-Work / save / re-load loop when a captured
    /// Book turns out to be a compendium of N Works.
    ///
    /// Rows must each be either an attach-existing row
    /// (<see cref="WorkFormViewModel.WorkFormInput.AttachedWorkId"/>
    /// non-null) or a populated new-work row (title + at least one
    /// contributor across <c>Authors</c> / <c>Contributors</c>, or
    /// covered by <c>SharedAuthors</c> when <paramref name="singleAuthor"/>
    /// is true). Empty rows are silently dropped; if no usable rows
    /// remain after filtering, throws <see cref="InvalidOperationException"/>
    /// with a user-facing message.
    ///
    /// Returns the count of Works actually attached (excludes already-
    /// attached existing rows, which are silently skipped — matches the
    /// "may already be on this book" semantic from the single-attach
    /// path). Refreshes the snapshot on success.</summary>
    public async Task<int> AttachMultipleWorksAsync(
        IReadOnlyList<WorkFormViewModel.WorkFormInput> rows,
        bool singleAuthor,
        bool singleGenre,
        IReadOnlyList<string> sharedAuthors,
        IReadOnlyList<int> sharedGenreIds)
    {
        if (Book is null) return 0;
        ArgumentNullException.ThrowIfNull(rows);

        // Map each Web row to the Application contract, parsing the free-text
        // date here (the VM owns parsing). The handler does the single-pass
        // author/genre resolution, the attach-existing vs create-new branch,
        // and the empty/no-contributor validation throws.
        var mappedRows = rows.Select(r =>
        {
            var pub = PartialDateParser.TryParse(r.FirstPublishedDate) ?? PartialDate.Empty;
            return new WorkRow(
                r.AttachedWorkId,
                r.Title,
                r.Subtitle,
                pub.Date,
                pub.Precision,
                r.Authors,
                r.Contributors.Select(c => new ContributorInput(c.Name, c.Role)).ToList(),
                r.GenreIds);
        }).ToList();

        var count = await dispatcher.Send(new AttachWorksToBook(
            Book.Id, mappedRows, singleAuthor, singleGenre, sharedAuthors, sharedGenreIds));
        await InitializeAsync(Book.Id);
        return count;
    }

    /// <summary>Remove a Work from this Book. If the Work isn'\''t attached
    /// to any other Book it'\''s deleted outright (orphan Works are noise);
    /// otherwise just detaches the join row so the Work continues to live
    /// on its other Books. Returns the Work's title on success, null when
    /// the Work or Book is gone. Refreshes the snapshot so the Works list
    /// re-renders without the removed Work.</summary>
    public async Task<string?> RemoveWorkFromBookAsync(int workId)
    {
        if (Book is null) return null;
        var title = await dispatcher.Send(new RemoveWorkFromBook(Book.Id, workId));
        if (title is not null) await InitializeAsync(Book.Id);
        return title;
    }

    /// <summary>Delete this Book. Soft-delete: the Book row is kept as
    /// a tombstone (DeletedAt = UtcNow, hidden from every normal query
    /// by the global HasQueryFilter) so the catalog snapshot delta path
    /// can emit it in <c>deletedIds[]</c> for Bookshelf clients. The
    /// aggregate children are hard-removed at the same save:
    ///   - Editions (+ Copies via cascade) get RemoveRange'd.
    ///   - Tags / Works join rows are cleared (skip-nav).
    /// Net visible behaviour matches the old hard-delete (book + its
    /// editions/copies/joins disappear) — only the husk row survives.
    /// Returns true on success so the caller can navigate away.</summary>
    public async Task<bool> DeleteBookAsync()
    {
        if (Book is null) return false;
        await dispatcher.Send(new DeleteBook(Book.Id));
        return true;
    }

    /// <summary>Tag autocomplete — returns existing tags not already assigned, filtered by substring.</summary>
    public async Task<IEnumerable<string>> SearchTagsAsync(string query, CancellationToken ct)
    {
        var assigned = CurrentTags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = await dispatcher.Query(new GetTagSuggestions(query ?? ""), ct);
        return names.Where(n => !assigned.Contains(n));
    }
}
