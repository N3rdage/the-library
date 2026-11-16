using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services.Covers;

/// <summary>An Edition or Book whose cover URL is due for a mirror attempt.</summary>
public sealed record CoverMirrorCandidate(int Id, string Url);

// The cover mirror's reads and writes against CoverMirrorState, kept apart
// from the hosted-service loop so they can be exercised against a real
// database directly.
//
// A row is due when its cover URL is non-empty and no state row holds it
// back. A state row holds a row back only while its Url still matches the
// row's current URL and either the last outcome was a permanent refusal or
// the transient-failure backoff (NextAttemptUtc) has not yet elapsed. A
// changed URL therefore makes the row due again at once.
internal static class CoverMirrorQueue
{
    public static Task<List<CoverMirrorCandidate>> DueEditionsAsync(BookTrackerDbContext db, DateTime nowUtc, CancellationToken ct) =>
        db.Editions
            .Where(e => e.CoverUrl != null && e.CoverUrl != "")
            .Where(e => !db.CoverMirrorStates.Any(s =>
                s.Target == CoverMirrorTarget.Edition
                && s.TargetId == e.Id
                && s.Url == e.CoverUrl
                && (s.LastOutcome == CoverMirrorOutcome.PermanentRefusal || s.NextAttemptUtc > nowUtc)))
            .OrderBy(e => e.Id)
            .Select(e => new CoverMirrorCandidate(e.Id, e.CoverUrl!))
            .ToListAsync(ct);

    public static Task<List<CoverMirrorCandidate>> DueBooksAsync(BookTrackerDbContext db, DateTime nowUtc, CancellationToken ct) =>
        db.Books
            .Where(b => b.DefaultCoverArtUrl != null && b.DefaultCoverArtUrl != "")
            .Where(b => !db.CoverMirrorStates.Any(s =>
                s.Target == CoverMirrorTarget.Book
                && s.TargetId == b.Id
                && s.Url == b.DefaultCoverArtUrl
                && (s.LastOutcome == CoverMirrorOutcome.PermanentRefusal || s.NextAttemptUtc > nowUtc)))
            .OrderBy(b => b.Id)
            .Select(b => new CoverMirrorCandidate(b.Id, b.DefaultCoverArtUrl!))
            .ToListAsync(ct);

    /// <summary>
    /// Records an unsuccessful attempt for <paramref name="url"/>. Attempts
    /// counts consecutive failures of the same URL and restarts at 1 when the
    /// URL differs from the one on the existing state row.
    /// </summary>
    public static async Task RecordFailureAsync(
        BookTrackerDbContext db,
        CoverMirrorTarget target,
        int targetId,
        string url,
        CoverMirrorOutcome outcome,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var state = await db.CoverMirrorStates
            .FirstOrDefaultAsync(s => s.Target == target && s.TargetId == targetId, ct);
        if (state is null)
        {
            state = new CoverMirrorState { Target = target, TargetId = targetId };
            db.CoverMirrorStates.Add(state);
        }

        state.Attempts = state.Url == url ? state.Attempts + 1 : 1;
        state.Url = url;
        state.LastAttemptUtc = nowUtc;
        state.NextAttemptUtc = nowUtc + CoverMirrorBackoff.Delay(state.Attempts);
        state.LastOutcome = outcome;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes the state row for a target, if any (after a successful mirror).</summary>
    public static Task ClearAsync(BookTrackerDbContext db, CoverMirrorTarget target, int targetId, CancellationToken ct) =>
        db.CoverMirrorStates
            .Where(s => s.Target == target && s.TargetId == targetId)
            .ExecuteDeleteAsync(ct);

    /// <summary>
    /// Deletes state rows whose Edition or Book no longer exists. Books are
    /// read through the soft-delete filter, so a tombstoned book's state goes too.
    /// </summary>
    public static async Task RemoveOrphansAsync(BookTrackerDbContext db, CancellationToken ct)
    {
        await db.CoverMirrorStates
            .Where(s => s.Target == CoverMirrorTarget.Edition && !db.Editions.Any(e => e.Id == s.TargetId))
            .ExecuteDeleteAsync(ct);
        await db.CoverMirrorStates
            .Where(s => s.Target == CoverMirrorTarget.Book && !db.Books.Any(b => b.Id == s.TargetId))
            .ExecuteDeleteAsync(ct);
    }
}
