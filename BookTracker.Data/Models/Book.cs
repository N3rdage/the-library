using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public enum BookStatus
{
    Unread,
    Reading,
    Read
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

    public BookStatus Status { get; set; } = BookStatus.Read;

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

    [MaxLength(500)]
    public string? DefaultCoverArtUrl { get; set; }

    public List<Edition> Editions { get; set; } = [];

    public List<Tag> Tags { get; set; } = [];

    public List<Work> Works { get; set; } = [];
}
