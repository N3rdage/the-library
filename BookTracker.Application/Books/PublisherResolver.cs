using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>
/// Find-or-create a <see cref="Publisher"/> by name within the given context.
/// Publisher is a flat lookup table with no invariants of its own
/// (convention C9), so it's resolved inline by the Edition handlers rather
/// than promoted to its own aggregate. A blank name resolves to null.
/// Public so the Add / Bulk Add ViewModels can route their inline
/// publisher find-or-creates through the single owner (TD-15b). Shares the
/// plain check-then-insert race tracked as TD-15 (single-user app, race
/// near-impossible); the unique-index-catch upgrade lands on all resolvers
/// together when concurrent writes arrive.
/// </summary>
public static class PublisherResolver
{
    public static async Task<Publisher?> ResolveAsync(
        BookTrackerDbContext db, string? name, CancellationToken ct = default)
    {
        var trimmed = name.TrimToNull();
        if (trimmed is null) return null;

        var existing = await db.Publishers.FirstOrDefaultAsync(p => p.Name == trimmed, ct);
        if (existing is not null) return existing;

        var publisher = new Publisher { Name = trimmed };
        db.Publishers.Add(publisher);
        return publisher;
    }
}
