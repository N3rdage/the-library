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
}
