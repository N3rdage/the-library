using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public class Genre
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int? ParentGenreId { get; set; }
    public Genre? ParentGenre { get; set; }
    /// <summary>EF fills it from each child's <see cref="ParentGenreId"/>.</summary>
    public IReadOnlyCollection<Genre> Children => _children;
    private readonly List<Genre> _children = [];

    /// <summary>EF fills it; written from the Work side through <see cref="Work.SetGenres"/>.</summary>
    public IReadOnlyCollection<Work> Works => _works;
    private readonly List<Work> _works = [];
}
