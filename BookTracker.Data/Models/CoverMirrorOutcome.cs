namespace BookTracker.Data.Models;

// Result of the last unsuccessful mirror attempt recorded on a
// CoverMirrorState. A transient failure is retried on a backoff; a permanent
// refusal is not retried until the row's cover URL changes.
public enum CoverMirrorOutcome
{
    TransientFailure = 0,
    PermanentRefusal = 1
}
