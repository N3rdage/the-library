using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public class Tag
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>EF fills it; written from the Book side through <see cref="Book.AddTag"/>.</summary>
    public IReadOnlyCollection<Book> Books => _books;
    private readonly List<Book> _books = [];
}
