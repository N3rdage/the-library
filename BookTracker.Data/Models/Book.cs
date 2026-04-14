using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public enum BookStatus
{
    Unread,
    Reading,
    Read
}

public class Book
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Author { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Genre { get; set; }

    public BookStatus Status { get; set; } = BookStatus.Unread;

    [Range(0, 5)]
    public int Rating { get; set; }

    public string? Notes { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? DefaultCoverArtUrl { get; set; }

    public List<BookCopy> Copies { get; set; } = [];
}
