using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Wishlist;

// Read-model behind the wishlist search's duplicate badge. Relocated from
// WishlistViewModel.FindDuplicateMatchesAsync in PR6b-5. For a given ISBN,
// reports whether it's already owned (any Edition.Isbn) and/or already on the
// wishlist (legacy single Isbn column OR the WishlistItemIsbn table). Doesn't
// block Add — just tells the user before they click.
public sealed record GetWishlistDuplicate(string Isbn) : IQuery<WishlistDuplicate>;

public record WishlistDuplicate(int? OwnedBookId, int? WishlistedItemId);

public sealed class GetWishlistDuplicateHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetWishlistDuplicate, WishlistDuplicate>
{
    public async Task<WishlistDuplicate> HandleAsync(GetWishlistDuplicate query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var ownedBookId = await db.Editions
            .Where(e => e.Isbn == query.Isbn)
            .Select(e => (int?)e.BookId)
            .FirstOrDefaultAsync(ct);

        var wishlistedItemId = await db.WishlistItems
            .Where(w => w.Isbn == query.Isbn || w.Isbns.Any(i => i.Isbn == query.Isbn))
            .Select(w => (int?)w.Id)
            .FirstOrDefaultAsync(ct);

        return new WishlistDuplicate(ownedBookId, wishlistedItemId);
    }
}
