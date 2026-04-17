using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public class Edition
{
    public int Id { get; set; }

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    [Required, MaxLength(20)]
    public string Isbn { get; set; } = string.Empty;

    public BookFormat Format { get; set; } = BookFormat.Softcopy;

    public DateOnly? DatePrinted { get; set; }

    [MaxLength(500)]
    public string? CoverUrl { get; set; }

    public int? PublisherId { get; set; }
    public Publisher? Publisher { get; set; }

    public List<Copy> Copies { get; set; } = [];
}
