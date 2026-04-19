using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// Marker rows for one-shot data operations that should run at most once per
// deployment (typically a hosted startup task that backfills data after a
// schema/semantic change). The Name uniquely identifies the operation
// (e.g. "BackfillEditionFormats-v1") so a duplicate startup is a no-op.
public class MaintenanceLog
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public DateTime CompletedAt { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }
}
