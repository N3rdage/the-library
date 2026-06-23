using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>
/// Enforces the one cross-row Series invariant the aggregate can't see on its
/// own: <c>Series.Name</c> is unique (a filtered unique index backs it). Lifted
/// out of the database's raw constraint violation into a user-safe
/// <see cref="DomainRuleException"/> so create/rename collisions surface as a
/// friendly snackbar instead of a 500. Shared by the create + update handlers.
/// </summary>
internal static class SeriesNameGuard
{
    public static async Task EnsureUniqueAsync(
        BookTrackerDbContext db, string name, int? excludeId, CancellationToken ct)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) return; // the aggregate owns the required-name rule

        var lower = trimmed.ToLower();
        var clashes = await db.Series
            .AnyAsync(s => s.Name.ToLower() == lower && (excludeId == null || s.Id != excludeId), ct);
        if (clashes)
            throw new DomainRuleException($"A series named “{trimmed}” already exists.");
    }
}
