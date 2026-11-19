using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public enum BookStatus
{
    Unread,
    Reading,
    Read,
    // Reference rows (dictionaries, encyclopaedias, field guides, style
    // guides, monographs) — opted out of the linear Unread/Reading/Read
    // arc since "Read" doesn't describe a dictionary. Still rateable
    // because a security reference or art monograph can absolutely be
    // better or worse than another. Home page "to read" counts skip
    // this status.
    Reference,
}

public enum BookCategory
{
    Fiction,
    NonFiction
}

// A Book is a physical-object grouping — what you hold and own. It carries
// the per-physical-book reading state (Status, Rating, Notes, Tags), the
// cover art, series membership (the book is installment N of a publication
// series — TODO #56 moved this Work→Book), and one or more Editions / Copies.
// Authorship, subtitle, and genres belong to the contained Works. For
// single-Work books (the common case) Book.Title mirrors the sole Work's title.
//
// The scalar fields are written only through the methods below (private
// setters; EF materialises through them regardless of accessibility), so a
// handler cannot bypass an invariant and the compiler enforces it. Id stays
// public for EF and the tests' in-memory graphs.
public class Book
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; private set; } = string.Empty;

    public BookCategory Category { get; private set; } = BookCategory.Fiction;

    public BookStatus Status { get; private set; } = BookStatus.Unread;

    [Range(0, 5)]
    public int Rating { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Stamped once, at creation; nothing in the app moves it.</summary>
    public DateTime DateAdded { get; private set; } = DateTime.UtcNow;

    /// <summary>UTC stamp updated whenever this Book OR any entity in
    /// its aggregate (Edition, Copy, Work, WorkAuthor, BookTag) changes.
    /// Backs the `GET /api/catalog-snapshot?since=<token>` delta query
    /// so Bookshelf refreshes can ship only changed Books instead of
    /// the full ~150KB snapshot. Bumped via BookUpdatedAtInterceptor at
    /// SaveChanges time — save sites don't need to set it explicitly.
    /// The setter is internal for that interceptor (same assembly); the
    /// tests reach it through InternalsVisibleTo to seed a watermark.</summary>
    public DateTime UpdatedAt { get; internal set; } = DateTime.UtcNow;

    /// <summary>Soft-delete tombstone. Non-null = the Book is "deleted"
    /// from the user's perspective; a global EF query filter
    /// (HasQueryFilter) hides these rows from every normal query, so
    /// the Library / View / search / merge surfaces all behave as if
    /// the row is gone. The husk row survives so the delta-sync
    /// endpoint can emit it as a tombstone in <c>deletedIds[]</c> for
    /// Bookshelf clients to drop from their local cache. Children
    /// (Editions, Copies, BookTag join, BookWork join) are
    /// hard-removed at delete time — the husk has no aggregate.
    /// Stamped by <see cref="SoftDelete"/>; the setter is internal so the
    /// tests can seed a tombstone directly (InternalsVisibleTo).</summary>
    public DateTime? DeletedAt { get; internal set; }

    [MaxLength(500)]
    public string? DefaultCoverArtUrl { get; private set; }

    // Collection setters stay public until the collections slice (C7):
    // seeds assign them wholesale, and AttachWork / AddEdition / SoftDelete
    // are the ordered write paths in the meantime.
    public List<Edition> Editions { get; set; } = [];

    public List<Tag> Tags { get; set; } = [];

    /// <summary>Skip-navigation over the Book↔Work join — convenient for "does
    /// this book contain work X" / search / aggregation queries. Does NOT carry
    /// the per-book display order; read <see cref="BookWorks"/> when order matters.
    /// <para><b>Read-only in practice — do NOT write to this.</b> Adding here
    /// (<c>book.Works.Add(work)</c> or a <c>Works = [...]</c> initializer) inserts
    /// a <see cref="BookWork"/> with Order defaulting to 0, colliding with the
    /// existing Order-0 row and corrupting display order. Route every attach
    /// through <see cref="AttachWork"/> so the work appends (Order = max + 1).
    /// (Setter stays public pending the C7 encapsulation lock-down.)</para></summary>
    public List<Work> Works { get; set; } = [];

    /// <summary>Explicit join rows carrying each Work's per-book display
    /// <see cref="BookWork.Order"/>. The canonical source for ordered display and
    /// the write path for appends (see <see cref="AttachWork"/>).</summary>
    public List<BookWork> BookWorks { get; set; } = [];

    // Series membership lives on the Book — a Book is installment N of a
    // publication series ("The Last Wish" is Witcher #1), whether it holds a
    // single Work or a whole short-story collection. The constituent Works
    // carry no series of their own. (Pre-Work-refactor this lived on Book;
    // it briefly moved to Work, then came back here once a series of
    // short-story-collection books proved unmanageable at Work grain.)
    public int? SeriesId { get; private set; }
    public Series? Series { get; private set; }

    /// <summary>Position in a Series (1-based). Defaults to publication order for Collections.</summary>
    public int? SeriesOrder { get; private set; }

    /// <summary>
    /// Optional human-facing order label that overrides <see cref="SeriesOrder"/>
    /// in the UI when the position isn't a plain integer — e.g. "4.5" for an
    /// interquel (<i>Edgedancer</i>) or "1A" for a hierarchical position.
    /// Null for ordinary integer orders, where <see cref="SeriesOrder"/> renders
    /// directly. Sorting + gap detection always run off the integer
    /// <see cref="SeriesOrder"/> (floored from the display value on capture).
    /// </summary>
    [MaxLength(50)]
    public string? SeriesOrderDisplay { get; private set; }

    // --- Aggregate behaviour -------------------------------------------------
    // Invariant-bearing operations live here so the rules are enforced in one
    // place and are unit-testable without EF. See docs/BACKEND-REFACTOR-DESIGN.md.
    // Every scalar write goes through these methods; the collections are the
    // remaining public surface (see the note above Editions).

    /// <summary>Creates a Book from the Add page's book fields. Title is required
    /// and trimmed; notes and cover trim to null. The rating is stored as given,
    /// the way the Add page has always stored it (<see cref="Rate"/> is the guarded
    /// 0–5 mutation). DateAdded keeps its creation-time default. Works, editions
    /// and series attach through their own methods.</summary>
    public static Book Create(
        string title,
        BookCategory category,
        BookStatus status,
        int rating,
        string? notes,
        string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainRuleException("Title is required.");
        return new Book
        {
            Title = title.Trim(),
            Category = category,
            Status = status,
            Rating = rating,
            Notes = notes.TrimToNull(),
            DefaultCoverArtUrl = coverUrl.TrimToNull(),
        };
    }

    /// <summary>Sets the 0–5 star rating; rejects out-of-range values.</summary>
    public void Rate(int rating)
    {
        if (rating is < 0 or > 5)
            throw new DomainRuleException("Rating must be between 0 and 5.");
        Rating = rating;
    }

    public void ChangeStatus(BookStatus status) => Status = status;

    public void UpdateNotes(string? notes) => Notes = notes.TrimToNull();

    /// <summary>Records the book as read in a single gesture — status, rating,
    /// and (optionally) notes together (the "mark read" quick action). One atomic
    /// command, not three field updates (convention C10). Rating is validated
    /// first so an invalid value leaves the book untouched. <paramref name="notes"/>
    /// of <c>null</c> means "no note supplied" and leaves any existing notes
    /// intact (the dialog doesn't surface them, so a blank field mustn't wipe
    /// them); an empty/whitespace string still clears them.</summary>
    public void MarkRead(int rating, string? notes)
    {
        Rate(rating);
        Status = BookStatus.Read;
        if (notes is not null) UpdateNotes(notes);
    }

    /// <summary>Updates the Book-level fields edited from the "edit details"
    /// dialog (title, category, default cover). Title is required.</summary>
    public void UpdateDetails(string title, BookCategory category, string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainRuleException("Title is required.");
        Title = title.Trim();
        Category = category;
        DefaultCoverArtUrl = coverUrl.TrimToNull();
    }

    /// <summary>Sets the default cover on its own — the cover mirror swapping an
    /// upstream URL for the blob URL, and a merge carrying the loser's cover to a
    /// coverless winner. Trims to null, as <see cref="UpdateDetails"/> does.</summary>
    public void SetCover(string? url) => DefaultCoverArtUrl = url.TrimToNull();

    public void AssignToSeries(int seriesId, int? order, string? orderDisplay)
    {
        SeriesId = seriesId;
        SetSeriesOrder(order, orderDisplay);
    }

    /// <summary>Assigns this Book to a Series that may not be saved yet (no id
    /// until SaveChanges) — EF fixes up <see cref="SeriesId"/> from the
    /// navigation, so the series and its members land in one save.</summary>
    public void AssignToSeries(Series series, int? order, string? orderDisplay)
    {
        Series = series;
        SetSeriesOrder(order, orderDisplay);
    }

    public void ClearSeries()
    {
        SeriesId = null;
        SeriesOrder = null;
        SeriesOrderDisplay = null;
    }

    /// <summary>Repositions this Book within its current series — the integer sort
    /// key plus an optional display override ("4.5") — without changing which
    /// series it belongs to. Used by the manage-series-from-the-series-page flow.</summary>
    public void SetSeriesOrder(int? order, string? orderDisplay)
    {
        SeriesOrder = order;
        SeriesOrderDisplay = orderDisplay;
    }

    /// <summary>Attaches <paramref name="work"/> to this Book, appending it to
    /// the end of the current work order (Order = max + 1, or 0 for the first).
    /// No-op returning false if the Work is already on this Book. This is the one
    /// write path that assigns <see cref="BookWork.Order"/> — every ordered
    /// append (create-work, attach-existing, merge) routes through here rather
    /// than the order-less Book.Works skip-navigation.
    ///
    /// Requires <see cref="BookWorks"/> to be loaded so the max is computed over
    /// the real set — callers Include(b =&gt; b.BookWorks). A new Book starts with
    /// an empty collection, which is correct (first work gets Order 0).</summary>
    public bool AttachWork(Work work)
    {
        // Dedup by id for saved works; by reference for a brand-new (Id 0) work
        // that can't already be attached but wouldn't match on id.
        if (BookWorks.Any(bw => bw.Work == work || (work.Id != 0 && bw.WorkId == work.Id)))
            return false;

        var next = BookWorks.Count == 0 ? 0 : BookWorks.Max(bw => bw.Order) + 1;
        var link = new BookWork { Book = this, Work = work, Order = next };
        BookWorks.Add(link);
        work.BookWorks.Add(link);
        return true;
    }

    /// <summary>Adds a new Edition seeded with its first Copy — an Edition
    /// always owns at least one Copy. Returns the new Edition.</summary>
    public Edition AddEdition(
        string? isbn,
        BookFormat format,
        int? editionNumber,
        DateOnly? datePrinted,
        DatePrecision datePrintedPrecision,
        string? coverUrl,
        Publisher? publisher,
        BookCondition firstCopyCondition)
    {
        var edition = new Edition
        {
            Isbn = isbn.TrimToNull(),
            Format = format,
            EditionNumber = editionNumber,
            DatePrinted = datePrinted,
            DatePrintedPrecision = datePrintedPrecision,
            Publisher = publisher,
            CoverUrl = coverUrl.TrimToNull(),
        };
        edition.AddCopy(firstCopyCondition, null, null);
        Editions.Add(edition);
        return edition;
    }

    /// <summary>Removes a Copy from the book. If it was the Edition's last
    /// Copy, the Edition goes too — an Edition with zero Copies represents
    /// nothing ownable. Throws if the Copy isn't part of this book.</summary>
    public void RemoveCopy(int copyId)
    {
        var edition = Editions.FirstOrDefault(e => e.Copies.Any(c => c.Id == copyId))
            ?? throw new DomainRuleException("That copy isn't part of this book.");
        if (edition.RemoveCopy(copyId))
            Editions.Remove(edition);
    }

    /// <summary>Soft-deletes the book: hard-removes the Editions (their Copies
    /// cascade), clears the Tag join rows, and stamps the tombstone. The husk row
    /// survives, hidden by the global query filter, so the delta-sync endpoint can
    /// emit it in <c>deletedIds[]</c>. Works are NOT touched here — they're a
    /// shared aggregate with a ref-count lifecycle, so DeleteBookHandler detaches
    /// them and deletes any that become orphaned.
    /// Throws while any copy is lent out: the open loan would vanish with the
    /// copy. Callers load Editions → Copies → Loans so the check sees them.</summary>
    public void SoftDelete()
    {
        if (Editions.Any(e => e.Copies.Any(c => c.OpenLoan is not null)))
            throw new DomainRuleException("A copy of this book is lent out. Return the loan first.");

        // Severing the required Book→Edition relationship orphan-deletes each
        // Edition (and its Copies cascade at the DB level) — same mechanism
        // RemoveCopy already relies on.
        Editions.Clear();
        Tags.Clear();
        DeletedAt = DateTime.UtcNow;
    }
}
