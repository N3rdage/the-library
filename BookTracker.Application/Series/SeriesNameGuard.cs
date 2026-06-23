using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>
/// Enforces the one cross-row Series invariant the aggregate can't see on its
/// own: <c>Series.Name</c> is unique (a unique index backs it). Turns the
/// database's raw constraint violation into a user-safe
/// <see cref="DomainRuleException"/> so create/rename collisions surface as a
/// friendly message instead of a 500. Shared by the create + update handlers.
///
/// Two layers: a pre-check (<see cref="EnsureUniqueAsync"/>) gives the clean
/// common-case message without a failed INSERT, and
/// <see cref="SaveTranslatingDuplicateAsync"/> makes the unique index itself the
/// authoritative backstop — closing the check-then-act race (and any divergence
/// between this comparison and the column's collation) where a duplicate slips
/// past the pre-check and would otherwise throw a raw <see cref="DbUpdateException"/>.
/// </summary>
internal static class SeriesNameGuard
{
    /// <summary>Pre-check: throws the friendly error if the name is already taken.</summary>
    public static async Task EnsureUniqueAsync(
        BookTrackerDbContext db, string name, int? excludeId, CancellationToken ct)
    {
        if (await IsNameTakenAsync(db, name, excludeId, ct))
            throw new DomainRuleException($"A series named “{name.Trim()}” already exists.");
    }

    /// <summary>Saves, translating a unique-index violation on the name into the
    /// friendly <see cref="DomainRuleException"/>. Any other failure propagates
    /// unchanged. Use in place of <c>db.SaveChangesAsync</c> on the create/rename path.</summary>
    public static async Task SaveTranslatingDuplicateAsync(
        BookTrackerDbContext db, string name, int? excludeId, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
            // Re-read against the DB (the failed row isn't persisted) to confirm
            // the violation was the name clash before claiming it as one.
            if (await IsNameTakenAsync(db, name, excludeId, ct))
                throw new DomainRuleException($"A series named “{name.Trim()}” already exists.");
            throw;
        }
    }

    private static async Task<bool> IsNameTakenAsync(
        BookTrackerDbContext db, string name, int? excludeId, CancellationToken ct)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) return false; // the aggregate owns the required-name rule

        var lower = trimmed.ToLower();
        return await db.Series
            .AnyAsync(s => s.Name.ToLower() == lower && (excludeId == null || s.Id != excludeId), ct);
    }
}
