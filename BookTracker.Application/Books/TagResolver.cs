using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>
/// Find-or-create a <see cref="Tag"/> by name within the given context. Tag is a
/// flat lookup table with no invariants of its own (convention C9), so it's
/// resolved inline by the tag-write handlers rather than promoted to its own
/// aggregate. This is the single owner of tag-name normalisation — names are
/// trimmed and lower-cased, so "Signed" and "signed" resolve to one row.
/// Mirrors <see cref="Authors.AuthorResolver"/> / <see cref="PublisherResolver"/>;
/// the check-then-insert race all three share is tracked as TD-15.
/// </summary>
public static class TagResolver
{
    public static async Task<Tag> FindOrCreateAsync(string name, BookTrackerDbContext db, CancellationToken ct = default)
    {
        var normalized = name?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException("Tag name must be non-empty.", nameof(name));

        var existing = await db.Tags.FirstOrDefaultAsync(t => t.Name == normalized, ct);
        if (existing is not null) return existing;

        var tag = new Tag { Name = normalized };
        db.Tags.Add(tag);
        return tag;
    }
}
