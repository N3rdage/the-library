using BookTracker.Application.Formatting;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Shared.Catalog;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Wishlist;

// Read-model for the /wishlist series-gaps section. Relocated verbatim from
// WishlistViewModel.LoadSeriesGapsAsync in PR6b-5. Two shapes in one read:
//   Gaps       — structured series (ExpectedCount set) with missing numbered slots.
//   OpenSeries — open-ended series (no ExpectedCount) the user owns ≥1 book in.
public sealed record GetSeriesGaps : IQuery<SeriesGapsResult>;

public record SeriesGapsResult(
    IReadOnlyList<SeriesGap> Gaps,
    IReadOnlyList<OpenSeries> OpenSeries);

public record SeriesGap(
    int SeriesId, string SeriesName, string? Author,
    int OwnedCount, int ExpectedCount,
    List<int> MissingPositions,
    List<OwnedSeriesBook> OwnedBooks);

public record OwnedSeriesBook(int Id, string Title, string? SeriesOrderLabel);

// Series with no ExpectedCount where the user owns at least one Book.
// HighestOwnedOrder seeds the "Add next N missing" flow (0 when no Books carry
// a SeriesOrder yet). OwnedOrders is the full owned set so the suggestion can
// skip already-owned slots when computing the next-N range.
public record OpenSeries(
    int SeriesId,
    string SeriesName,
    string? Author,
    int OwnedCount,
    int HighestOwnedOrder,
    IReadOnlyList<int> OwnedOrders);

public sealed class GetSeriesGapsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetSeriesGaps, SeriesGapsResult>
{
    public async Task<SeriesGapsResult> HandleAsync(GetSeriesGaps query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Structured series with a known expected count where we're missing books.
        // Series membership is a per-Book concept — the Book is installment N.
        // The Books navigation is filtered by the global soft-delete query
        // filter, so tombstoned books don't count as owned.
        var incompleteSeries = await db.Series
            .Include(s => s.Books)
            .Where(s => s.Type == SeriesType.Series && s.ExpectedCount != null)
            .ToListAsync(ct);

        var gaps = incompleteSeries
            .Select(s =>
            {
                // Only true numbered volumes occupy a slot (shared rule —
                // SeriesSlots.OccupiesNumberedSlot). A floored interquel ("4.5",
                // SeriesOrderDisplay set) must not count as owning slot #4, or
                // the real #4 gap is silently hidden.
                var ownedPositions = s.Books
                    .Where(b => SeriesSlots.OccupiesNumberedSlot(b.SeriesOrder, b.SeriesOrderDisplay))
                    .Select(b => b.SeriesOrder!.Value)
                    .Where(o => o <= s.ExpectedCount!.Value)
                    .ToHashSet();

                var missing = new List<int>();
                for (int i = 1; i <= s.ExpectedCount!.Value; i++)
                {
                    if (!ownedPositions.Contains(i))
                        missing.Add(i);
                }

                return new SeriesGap(
                    s.Id,
                    s.Name,
                    s.Author,
                    ownedPositions.Count,
                    s.ExpectedCount.Value,
                    missing,
                    s.Books.OrderBy(b => b.SeriesOrder ?? int.MaxValue)
                        .Select(b => new OwnedSeriesBook(
                            b.Id,
                            b.Title,
                            SeriesOrderParser.Format(b.SeriesOrder, b.SeriesOrderDisplay)))
                        .ToList());
            })
            .Where(g => g.MissingPositions.Count > 0)
            .OrderBy(g => g.SeriesName)
            .ToList();

        // Open-ended series — no ExpectedCount, but the user owns at least one
        // numbered book. Highest-owned-order seeds the suggestion; series with
        // all-null SeriesOrder still surface (HighestOwnedOrder=0).
        var openSeries = await db.Series
            .Include(s => s.Books)
            .Where(s => s.Type == SeriesType.Series && s.ExpectedCount == null && s.Books.Any())
            .ToListAsync(ct);

        var open = openSeries
            .OrderBy(s => s.Name)
            .Select(s =>
            {
                var orders = s.Books
                    .Where(b => b.SeriesOrder.HasValue)
                    .Select(b => b.SeriesOrder!.Value)
                    .OrderBy(n => n)
                    .ToList();
                return new OpenSeries(
                    s.Id,
                    s.Name,
                    s.Author,
                    s.Books.Count,
                    orders.Count == 0 ? 0 : orders.Max(),
                    orders);
            })
            .ToList();

        return new SeriesGapsResult(gaps, open);
    }
}
