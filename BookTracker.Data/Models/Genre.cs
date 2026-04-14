using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public class Genre
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public List<Book> Books { get; set; } = [];
}
