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

    [MaxLength(500)]
    public string? CoverUrl { get; set; }

    public int? PublisherId { get; set; }
    public Publisher? Publisher { get; set; }

    public List<Copy> Copies { get; set; } = [];
}
