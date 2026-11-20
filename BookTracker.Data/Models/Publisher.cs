using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public class Publisher
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>EF fills it from each Edition's PublisherId.</summary>
    public IReadOnlyCollection<Edition> Editions => _editions;
    private readonly List<Edition> _editions = [];
}
