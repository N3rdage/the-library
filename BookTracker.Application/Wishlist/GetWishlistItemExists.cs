using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Wishlist;

/// <summary>Whether a wishlist item with exactly this Title and Author already
/// exists (compared in SQL, so the database collation decides case). The AI
/// assistant checks it before adding a recommendation. Relocated from
/// AIAssistantViewModel.AddRecommendationToWishlistAsync.</summary>
public sealed record GetWishlistItemExists(string Title, string Author) : IQuery<bool>;

public sealed class GetWishlistItemExistsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetWishlistItemExists, bool>
{
    public async Task<bool> HandleAsync(GetWishlistItemExists query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WishlistItems
            .AsNoTracking()
            .AnyAsync(w => w.Title == query.Title && w.Author == query.Author, ct);
    }
}
