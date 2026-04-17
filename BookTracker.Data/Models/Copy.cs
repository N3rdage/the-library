namespace BookTracker.Data.Models;

public class Copy
{
    public int Id { get; set; }

    public int EditionId { get; set; }
    public Edition Edition { get; set; } = null!;

    public BookCondition Condition { get; set; } = BookCondition.Good;

    public DateTime? DateAcquired { get; set; }

    public string? Notes { get; set; }
}
