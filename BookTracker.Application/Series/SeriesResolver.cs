using BookTracker.Application.Formatting;
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
/// factory (Type=Series, no expected count) so the find-or-create path
/// doesn't re-spell the ExpectedCount/Type invariant. This covers
/// find-or-create only — <c>AIAssistantViewModel</c> still constructs a
/// Series directly for its create-collection-with-extra-fields flow, so the
/// factory isn't the *sole* Series-creation path. Shares the plain
/// check-then-insert race tracked as TD-15 (single-user app, race
/// near-impossible).
/// </summary>
public static class SeriesResolver
{
    public static async Task<SeriesAggregate?> ResolveAsync(
        BookTrackerDbContext db, string? name, CancellationToken ct = default)
    {
        var trimmed = name.TrimToNull();
        if (trimmed is null) return null;

        // Case-insensitive matching is delegated to the DB's (CI) collation
        // rather than an explicit `Name.ToLower() == name.ToLower()`: it keeps
        // this consistent with AuthorResolver/PublisherResolver and stays
        // sargable (seeks IX on Name — LOWER() on the column would force a
        // scan). Real SQL Server + Azure SQL default to CI; a case-sensitive
        // collation would split rows here (and equally in the sibling
        // resolvers), so fix it uniformly across all resolvers if it changes.
        var existing = await db.Series.FirstOrDefaultAsync(s => s.Name == trimmed, ct);
        if (existing is not null) return existing;

        var series = SeriesAggregate.Create(trimmed, null, SeriesType.Series, null, null);
        db.Series.Add(series);
        return series;
    }

    /// <summary>Attaches a Book to the series the user accepted on the Add / Bulk
    /// Add page — an existing pick by id, else find-or-create by name — parsing the
    /// free-text order label into the (sort int, display override) pair. No-op when
    /// nothing was accepted (null id + blank name). Series membership is a per-Book
    /// concept (the book is installment N), so this is the single place both
    /// single-add and bulk-add reconcile it.</summary>
    public static async Task AttachToBookAsync(
        BookTrackerDbContext db,
        Book book,
        int? acceptedSeriesId,
        string? acceptedSeriesName,
        string? orderLabel,
        CancellationToken ct = default)
    {
        // Derive the (sort int, display) pair from the captured label at save
        // time — never freeze the int at accept time.
        var (order, orderDisplay) = SeriesOrderParser.Parse(orderLabel);
        if (acceptedSeriesId is int existingId)
        {
            book.AssignToSeries(existingId, order, orderDisplay);
        }
        else if (!string.IsNullOrWhiteSpace(acceptedSeriesName))
        {
            // No resolved id — find-or-create by name (covers an eager-create that
            // was skipped/failed). New series default to SeriesType.Series; flip to
            // Collection on /series/{id} later.
            book.Series = await ResolveAsync(db, acceptedSeriesName, ct);
            book.SeriesOrder = order;
            book.SeriesOrderDisplay = orderDisplay;
        }
    }
}
