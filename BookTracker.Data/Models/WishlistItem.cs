using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BookTracker.Data.Models;

public enum WishlistPriority
{
    Low,
    Medium,
    High
}

public class WishlistItem
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Author { get; set; } = string.Empty;

    public WishlistPriority Priority { get; set; } = WishlistPriority.Medium;

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Price { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
