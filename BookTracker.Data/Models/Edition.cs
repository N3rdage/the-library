using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public class Edition
{
    public int Id { get; set; }

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    // Nullable to support pre-1974 books that predate ISBN. The unique index
    // in BookTrackerDbContext is filtered (WHERE Isbn IS NOT NULL) so any
    // number of no-ISBN editions can coexist without colliding.
    [MaxLength(20)]
    public string? Isbn { get; set; }

    public BookFormat Format { get; set; } = BookFormat.TradePaperback;

    public DateOnly? DatePrinted { get; set; }

    /// <summary>How precise <see cref="DatePrinted"/> is — drives display formatting.</summary>
    public DatePrecision DatePrintedPrecision { get; set; } = DatePrecision.Day;

    [MaxLength(500)]
    public string? CoverUrl { get; set; }

    /// <summary>
    /// True when <see cref="CoverUrl"/> points at a user-uploaded photo
    /// (typically because no online cover was available for a rare or old
    /// edition). Mirrored covers from upstream providers stay false. Used
    /// to (a) flag the cover with a small "your photo" badge in display
    /// surfaces, and (b) skip user-supplied covers if a future re-mirror
    /// pass re-fetches upstream URLs en masse.
    /// </summary>
    public bool IsUserSupplied { get; set; }

    public int? PublisherId { get; set; }
    public Publisher? Publisher { get; set; }

    public List<Copy> Copies { get; set; } = [];
}
