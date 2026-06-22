using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>
/// Find-or-create a <see cref="Publisher"/> by name within the given context.
/// Publisher is a flat lookup table with no invariants of its own
/// (convention C9), so it's resolved inline by the Edition handlers rather
/// than promoted to its own aggregate. A blank name resolves to null.
/// </summary>
internal static class PublisherResolver
{
    public static async Task<Publisher?> ResolveAsync(
        BookTrackerDbContext db, string? name, CancellationToken ct)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        var existing = await db.Publishers.FirstOrDefaultAsync(p => p.Name == trimmed, ct);
        if (existing is not null) return existing;

        var publisher = new Publisher { Name = trimmed };
        db.Publishers.Add(publisher);
        return publisher;
    }
}
