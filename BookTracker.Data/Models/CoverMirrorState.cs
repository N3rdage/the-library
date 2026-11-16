using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// Mirror bookkeeping for one cover URL that the cover mirror could not move
// into blob storage. One row per (Target, TargetId); the row is deleted when
// the cover mirrors successfully.
//
// No FK to the Edition or Book (polymorphic via Target); rows whose target no
// longer exists are swept by the mirror service. Not part of the Book
// aggregate, so a change here never bumps Book.UpdatedAt.
public class CoverMirrorState
{
    public int Id { get; set; }

    public CoverMirrorTarget Target { get; set; }

    public int TargetId { get; set; }

    // The URL this state is about. When the row's stored cover URL differs,
    // the state is stale and the row is due for mirroring again.
    [Required, MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public DateTime LastAttemptUtc { get; set; }

    public DateTime NextAttemptUtc { get; set; }

    public CoverMirrorOutcome LastOutcome { get; set; }
}
