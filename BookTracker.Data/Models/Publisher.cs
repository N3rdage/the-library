using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public class Publisher
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public List<Edition> Editions { get; set; } = [];
}
