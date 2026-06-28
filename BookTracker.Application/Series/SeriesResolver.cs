using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
// The feature folder is Series/, so the namespace collides with the Series
// *type* (see CreateSeries). Alias the entity wherever its static surface or
// the return type is needed.
using SeriesAggregate = BookTracker.Data.Models.Series;

namespace BookTracker.Application.Series;

/// <summary>
/// Find-or-create a <see cref="SeriesAggregate"/> by name within the given
/// context, mirroring <see cref="Authors.AuthorResolver"/> /
/// <see cref="Books.PublisherResolver"/>. Used by the Add / Bulk Add save
/// paths to attach a Work to a series the user accepted by free-text name.
/// A blank name resolves to null. New rows are created through the aggregate
/// factory (Type=Series, no expected count) so the ExpectedCount/Type
/// invariant is enforced in one place rather than re-spelled at each call
/// site. Shares the plain check-then-insert race tracked as TD-15
/// (single-user app, race near-impossible).
/// </summary>
public static class SeriesResolver
{
    public static async Task<SeriesAggregate?> ResolveAsync(
        BookTrackerDbContext db, string? name, CancellationToken ct = default)
    {
        var trimmed = name.TrimToNull();
        if (trimmed is null) return null;

        // SQL Server's default collation is case-insensitive, so == matches
        // the prior `Name.ToLower() == name.ToLower()` find at the call sites.
        var existing = await db.Series.FirstOrDefaultAsync(s => s.Name == trimmed, ct);
        if (existing is not null) return existing;

        var series = SeriesAggregate.Create(trimmed, null, SeriesType.Series, null, null);
        db.Series.Add(series);
        return series;
    }
}
