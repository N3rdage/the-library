using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public class Genre
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int? ParentGenreId { get; set; }
    public Genre? ParentGenre { get; set; }
    public List<Genre> Children { get; set; } = [];

    public List<Work> Works { get; set; } = [];
}
