using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public enum BookFormat
{
    Hardcopy,
    Softcopy
}

// Standard used-book grading scale, best → worst.
public enum BookCondition
{
    AsNew,
    Fine,
    VeryGood,
    Good,
    Fair,
    Poor
}

public class BookCopy
{
    public int Id { get; set; }

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    [Required, MaxLength(20)]
    public string Isbn { get; set; } = string.Empty;

    public BookFormat Format { get; set; } = BookFormat.Softcopy;

    public DateOnly? DatePrinted { get; set; }

    public BookCondition Condition { get; set; } = BookCondition.Good;

    [MaxLength(500)]
    public string? CustomCoverArtUrl { get; set; }

    public int? PublisherId { get; set; }
    public Publisher? Publisher { get; set; }
}
