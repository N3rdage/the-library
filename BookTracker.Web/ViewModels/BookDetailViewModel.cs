using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Loans;
using BookTracker.Application.Wishlist;
using BookTracker.Application.Works;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.Services.Covers;
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
public enum WishlistOfferOutcome { Added, AlreadyOnWishlist, NotAdded }

public class BookDetailViewModel(
    CoverPhotoUploader coverUploader,
    IDispatcher dispatcher,
    TimeProvider clock) : DispatchingViewModel
{
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
    // Copies still in the collection; removed ones are counted apart.
    public int TotalCopies => OwnedCopies.Count();
    public int RemovedCopies => AllCopies.Count(c => !c.IsOwned);

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
        if (await DispatchAsync(() => dispatcher.Send(new RateBook(Book.Id, rating))))
            CurrentRating = rating;
    }

    public async Task SetStatusAsync(BookStatus status)
    {
        if (Book is null) return;
        if (await DispatchAsync(() => dispatcher.Send(new SetBookStatus(Book.Id, status))))
            CurrentStatus = status;
    }

    public void MarkNotesDirty() => NotesDirty = true;

    public async Task SaveNotesAsync()
    {
        if (Book is null || !NotesDirty) return;

        NotesSaving = true;
        try
        {
            if (await DispatchAsync(() => dispatcher.Send(new UpdateBookNotes(Book.Id, CurrentNotes))))
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

        // Dedup against tags already on the book, case-insensitively, so a
        // casing variant of an existing chip can't be re-added. (TagResolver
        // owns storage-side normalisation; here we only need to match.)
        var trimmed = name.Trim();
        if (CurrentTags.Any(t => t.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            // Nothing to dispatch, but the attempt still clears an earlier refusal.
            Error = null;
            return null;
        }

        // The handler returns the tag's actual stored Name — which can differ in
        // casing from the input when it resolved to an existing row — so the
        // optimistic chip matches what a reload will show.
        var detail = await DispatchAsync(() => dispatcher.Send(new AddTagToBook(Book.Id, name)));
        if (detail is null) return null;

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
        await DispatchAsync(() => dispatcher.Send(new DeleteCopy(Book.Id, copyId)));
    }

    /// <summary>Today's date for loans (see <see cref="LoanDates.Today"/>).</summary>
    public DateOnly Today => LoanDates.Today(clock);

    public bool IsOverdue(CopyLoanDetail loan) => LoanDates.IsOverdue(loan.DueOn, Today);

    /// <summary>The copy as the first entry of the lend dialog's list; null
    /// when the copy is not on this book.</summary>
    public LendDialogViewModel.LendCopyOption? LendOptionFor(int copyId)
    {
        if (Book is null || Find(copyId) is not var (edition, copy) || !copy.IsOwned) return null;
        return new(copy.Id, Book.Title, EditionLabels.For(edition.Format, edition.Isbn), copy.Condition);
    }

    /// <summary>"Dune · Hardcover · 9780441013593": names the copy in the remove
    /// dialog; null when the copy is not on this book.</summary>
    public string? CopyLabelFor(int copyId) =>
        Book is not null && Find(copyId) is var (edition, _)
            ? $"{Book.Title} · {EditionLabels.For(edition.Format, edition.Isbn)}"
            : null;

    private (EditionDetail Edition, CopyDetail Copy)? Find(int copyId)
    {
        foreach (var edition in Book?.Editions ?? [])
        {
            var copy = edition.Copies.FirstOrDefault(c => c.Id == copyId);
            if (copy is not null) return (edition, copy);
        }
        return null;
    }

    private IEnumerable<CopyDetail> AllCopies => Book?.Editions.SelectMany(e => e.Copies) ?? [];

    private IEnumerable<CopyDetail> OwnedCopies => AllCopies.Where(c => c.IsOwned);

    /// <summary>The copy the header "Lend" seeds the dialog with: the first owned
    /// copy not on loan; null when every owned copy is out or the book has none.</summary>
    public int? FirstFreeCopyId => OwnedCopies.FirstOrDefault(c => c.OpenLoan is null)?.Id;

    /// <summary>True when the book has owned copies and every one of them is on loan.</summary>
    public bool AllCopiesLent => TotalCopies > 0 && FirstFreeCopyId is null;

    /// <summary>"Lent to Ann, Bob": the distinct borrowers in copy order; null
    /// unless every owned copy is on loan.</summary>
    public string? LentToLabel => AllCopiesLent
        ? "Lent to " + string.Join(", ", OwnedCopies.Select(c => c.OpenLoan!.BorrowerName).Distinct(StringComparer.Ordinal))
        : null;

    /// <summary>Brings a removed copy back into the collection and reloads the
    /// detail. False, with <see cref="DispatchingViewModel.Error"/> set, when
    /// the restore was refused.</summary>
    public async Task<bool> RestoreCopyAsync(int copyId)
    {
        if (Book is null) return false;
        if (!await DispatchAsync(() => dispatcher.Send(new RestoreCopyToCollection(copyId)))) return false;
        await InitializeAsync(Book.Id);
        return true;
    }

    /// <summary>A copy lost or damaged is probably wanted again, so the page
    /// offers the wishlist for those two reasons only.</summary>
    public static bool ShouldOfferWishlist(CopyDisposition reason) =>
        reason is CopyDisposition.Lost or CopyDisposition.Damaged;

    /// <summary>Adds the book of a removed copy to the wishlist at medium
    /// priority, with the copy's ISBN and the book's cover, unless an entry for
    /// that ISBN or that title and author is already there. A refusal sets
    /// <see cref="DispatchingViewModel.Error"/> and returns NotAdded.</summary>
    public async Task<WishlistOfferOutcome> AddToWishlistAsync(int copyId, CancellationToken ct = default)
    {
        if (Book is null || Find(copyId) is not var (edition, _)) return WishlistOfferOutcome.NotAdded;
        var book = Book;

        // The wishlist stores a normalised author, so the duplicate check asks with the same one.
        var author = WishlistItem.NormaliseAuthor(book.Works.FirstOrDefault()?.AuthorName);
        var outcome = await DispatchAsync<WishlistOfferOutcome?>(async () =>
        {
            var duplicate = edition.Isbn is { } isbn
                && (await dispatcher.Query(new GetWishlistDuplicate(isbn), ct)).WishlistedItemId is not null;
            if (duplicate || await dispatcher.Query(new GetWishlistItemExists(book.Title, author), ct))
                return WishlistOfferOutcome.AlreadyOnWishlist;

            await dispatcher.Send(new AddWishlistItem(
                book.Title, author, WishlistPriority.Medium,
                edition.Isbn is { } code ? [code] : [],
                book.CoverUrl ?? edition.CoverUrl), ct);
            return WishlistOfferOutcome.Added;
        });
        return outcome ?? WishlistOfferOutcome.NotAdded;
    }

    /// <summary>Marks every open loan on the book returned today, then reloads
    /// the detail once (also when one return is refused, so the page shows what
    /// is still out). False, with <see cref="DispatchingViewModel.Error"/> set,
    /// when a return was refused.</summary>
    public async Task<bool> ReturnAllLoansAsync()
    {
        if (Book is null) return false;
        var loanIds = AllCopies.Where(c => c.OpenLoan is not null).Select(c => c.OpenLoan!.LoanId).ToList();
        try
        {
            return await DispatchAsync(async () =>
            {
                foreach (var loanId in loanIds)
                    await dispatcher.Send(new ReturnLoan(loanId, Today));
            });
        }
        finally
        {
            await InitializeAsync(Book.Id);
        }
    }

    /// <summary>Marks the loan returned today and reloads the detail. False,
    /// with <see cref="DispatchingViewModel.Error"/> set, when the return was
    /// refused.</summary>
    public async Task<bool> ReturnLoanAsync(int loanId)
    {
        if (Book is null) return false;
        if (!await DispatchAsync(() => dispatcher.Send(new ReturnLoan(loanId, Today)))) return false;
        await InitializeAsync(Book.Id);
        return true;
    }


    /// <summary>
    /// Uploads a user-supplied photo as the cover for a specific Edition,
    /// replacing any existing cover. Used for rare / old editions where no
    /// online cover is available. Returns a <see cref="CoverUploadResult"/>
    /// with success/error shape so the page can render the right snackbar.
    /// </summary>
    public async Task<CoverUploadResult> UploadEditionCoverAsync(int editionId, CoverPhoto photo, CancellationToken ct)
    {
        if (Book is null) return CoverUploadResult.Failure("Book not loaded.");

        var upload = await coverUploader.UploadAsync(photo.Bytes, photo.ContentType, editionId, ct);
        if (!upload.Success) return upload;

        if (!await DispatchAsync(() => dispatcher.Send(new SetEditionCover(editionId, upload.NewUrl!, IsUserSupplied: true), ct)))
        {
            return CoverUploadResult.Failure(Error!);
        }

        // Refresh the in-memory snapshot so the page re-renders against the new URL.
        await InitializeAsync(Book.Id);

        return upload;
    }

    public async Task RemoveTagAsync(int tagId)
    {
        if (Book is null) return;

        if (await DispatchAsync(() => dispatcher.Send(new RemoveTagFromBook(Book.Id, tagId))))
            CurrentTags.RemoveAll(t => t.Id == tagId);
    }

    /// <summary>Search Works to attach to this Book — used by the
    /// AddExistingWorkDialog. Already-attached Works are filtered out
    /// server-side so re-attach attempts can't reach the dialog.</summary>
    public Task<IReadOnlyList<WorkSearchResult>> SearchAttachableWorksAsync(string query, CancellationToken ct)
    {
        if (Book is null) return Task.FromResult<IReadOnlyList<WorkSearchResult>>([]);
        return dispatcher.Query(new SearchWorks(query, ExcludeBookId: Book.Id, Max: 20), ct);
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
        var title = await DispatchAsync(() => dispatcher.Send(new AttachWorkToBook(Book.Id, workId)));
        if (title is not null) await InitializeAsync(Book.Id);
        else Error ??= "Couldn't attach that work — it may already be on this book.";
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

        var id = await DispatchAsync(() => dispatcher.Send(new CreateWorkOnBook(
            Book.Id, title, subtitle, authorNames, contributorInputs,
            firstPub.Date, firstPub.Precision, genreIds)));
        if (id is not null) await InitializeAsync(Book.Id);
        else Error ??= "Couldn't save the work — check that you provided a title and at least one contributor.";
        return id;
    }

    /// <summary>Attach a batch of new + existing Works to this Book in
    /// one transaction. Mirrors <see cref="CreateBookHandler"/>'s
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
    /// remain after filtering, sets <see cref="DispatchingViewModel.Error"/>
    /// to the handler's user-facing message and returns 0.
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

        var count = await DispatchAsync(() => dispatcher.Send(new AttachWorksToBook(
            Book.Id, mappedRows, singleAuthor, singleGenre, sharedAuthors, sharedGenreIds)));
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
        var title = await DispatchAsync(() => dispatcher.Send(new RemoveWorkFromBook(Book.Id, workId)));
        if (title is not null) await InitializeAsync(Book.Id);
        else Error ??= "Couldn't remove the work.";
        return title;
    }

    /// <summary>Persist a new display order for this Book's Works.
    /// <paramref name="orderedWorkIds"/> is the full sequence the user arranged
    /// (drag or type-a-position — both collapse to "the new order"). Refreshes
    /// the snapshot so the Works list re-renders in the saved order.</summary>
    public async Task ReorderWorksAsync(IReadOnlyList<int> orderedWorkIds)
    {
        if (Book is null) return;
        ArgumentNullException.ThrowIfNull(orderedWorkIds);
        if (!await DispatchAsync(() => dispatcher.Send(new ReorderWorks(Book.Id, orderedWorkIds)))) return;
        await InitializeAsync(Book.Id);
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
        return await DispatchAsync(() => dispatcher.Send(new DeleteBook(Book.Id)));
    }

    /// <summary>Tag autocomplete — returns existing tags not already assigned, filtered by substring.</summary>
    public async Task<IEnumerable<string>> SearchTagsAsync(string query, CancellationToken ct)
    {
        var assigned = CurrentTags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = await dispatcher.Query(new GetTagSuggestions(query ?? ""), ct);
        return names.Where(n => !assigned.Contains(n));
    }
}
