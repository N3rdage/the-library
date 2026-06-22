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
// cover art, and one or more Editions / Copies. Authorship, subtitle,
// genres, and series membership belong to the contained Works (PR 2 of
// the Work refactor moved these). For single-Work books (the common
// case) Book.Title mirrors the sole Work's title.
public class Book
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    public BookCategory Category { get; set; } = BookCategory.Fiction;

    public BookStatus Status { get; set; } = BookStatus.Unread;

    [Range(0, 5)]
    public int Rating { get; set; }

    public string? Notes { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    /// <summary>UTC stamp updated whenever this Book OR any entity in
    /// its aggregate (Edition, Copy, Work, WorkAuthor, BookTag) changes.
    /// Backs the `GET /api/catalog-snapshot?since=<token>` delta query
    /// so Bookshelf refreshes can ship only changed Books instead of
    /// the full ~150KB snapshot. Bumped via BookUpdatedAtInterceptor at
    /// SaveChanges time — save sites don't need to set it explicitly.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-delete tombstone. Non-null = the Book is "deleted"
    /// from the user's perspective; a global EF query filter
    /// (HasQueryFilter) hides these rows from every normal query, so
    /// the Library / View / search / merge surfaces all behave as if
    /// the row is gone. The husk row survives so the delta-sync
    /// endpoint can emit it as a tombstone in <c>deletedIds[]</c> for
    /// Bookshelf clients to drop from their local cache. Children
    /// (Editions, Copies, BookTag join, BookWork join) are
    /// hard-removed at delete time — the husk has no aggregate.</summary>
    public DateTime? DeletedAt { get; set; }

    [MaxLength(500)]
    public string? DefaultCoverArtUrl { get; set; }

    public List<Edition> Editions { get; set; } = [];

    public List<Tag> Tags { get; set; } = [];

    public List<Work> Works { get; set; } = [];

    // --- Aggregate behaviour -------------------------------------------------
    // Invariant-bearing operations live here so the rules are enforced in one
    // place and are unit-testable without EF. See docs/BACKEND-REFACTOR-DESIGN.md.
    // (Collection setters stay public for now — the C7 encapsulation lock-down
    // is deferred until every writer routes through these methods.)

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
    /// and notes together (the "mark read" quick action). One atomic command,
    /// not three field updates (convention C10). Rating is validated first so
    /// an invalid value leaves the book untouched.</summary>
    public void MarkRead(int rating, string? notes)
    {
        Rate(rating);
        Status = BookStatus.Read;
        UpdateNotes(notes);
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

    /// <summary>Adds a new Edition seeded with its first Copy — an Edition
    /// always owns at least one Copy. Returns the new Edition.</summary>
    public Edition AddEdition(
        string? isbn,
        BookFormat format,
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
    /// cascade), clears the Work/Tag join rows, and stamps the tombstone. The
    /// husk row survives, hidden by the global query filter, so the delta-sync
    /// endpoint can emit it in <c>deletedIds[]</c>. The handler need only load
    /// the children (so EF tracks the removals) and save.</summary>
    public void SoftDelete()
    {
        // Severing the required Book→Edition relationship orphan-deletes each
        // Edition (and its Copies cascade at the DB level) — same mechanism
        // RemoveCopy already relies on. Keeps the whole soft-delete on the
        // aggregate rather than splitting it with the handler.
        Editions.Clear();
        Works.Clear();
        Tags.Clear();
        DeletedAt = DateTime.UtcNow;
    }
}
