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

    /// <summary>
    /// Edition revision number — 1 for first edition, 2 for the revised
    /// second, etc. Lets the catalogue distinguish e.g. *Joy of Cooking
    /// 3rd ed. (1975)* from *Joy of Cooking 8th ed. (2019)*, which are
    /// materially different content despite sharing a Work. Nullable
    /// because most editions don't surface this on the cover (mass-
    /// market fiction reprints, pre-numbered classics).
    /// </summary>
    public int? EditionNumber { get; set; }

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
