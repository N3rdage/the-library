using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Wishlist;

// Read-model for the /wishlist list. Relocated from
// WishlistViewModel.LoadWishlistAsync in PR6b-5. Rows ordered by priority
// (high first) then title.
public sealed record GetWishlist : IQuery<IReadOnlyList<WishlistRow>>;

public record WishlistRow(
    int Id, string Title, string Author, WishlistPriority Priority,
    string? Isbn, string? CoverUrl, string? SeriesName, int? SeriesOrder);

public sealed class GetWishlistHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetWishlist, IReadOnlyList<WishlistRow>>
{
    public async Task<IReadOnlyList<WishlistRow>> HandleAsync(GetWishlist query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WishlistItems
            .Include(w => w.Series)
            .OrderByDescending(w => w.Priority)
            .ThenBy(w => w.Title)
            .Select(w => new WishlistRow(
                w.Id, w.Title, w.Author, w.Priority, w.Isbn,
                w.CoverUrl,
                w.Series != null ? w.Series.Name : null,
                w.SeriesOrder))
            .ToListAsync(ct);
    }
}
