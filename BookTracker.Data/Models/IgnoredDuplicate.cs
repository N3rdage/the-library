using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// Records user-dismissed duplicate-candidate pairs so the /duplicates page
// doesn't keep resurfacing false positives.
//
// IDs are stored with LowerId &lt; HigherId so (A,B) and (B,A) normalise to the
// same row. No FK to the referenced entity (polymorphic across four types
// via EntityType) — stale rows are filtered out by the detection service
// when either side no longer exists.
public class IgnoredDuplicate
{
    public int Id { get; set; }

    public DuplicateEntityType EntityType { get; set; }

    public int LowerId { get; set; }

    public int HigherId { get; set; }

    public DateTime DismissedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(1000)]
    public string? Note { get; set; }
}
