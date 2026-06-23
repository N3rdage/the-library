using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Wishlist;

/// <summary>Removes a wishlist item. Its <c>WishlistItemIsbn</c> children
/// cascade-delete via the FK. Idempotent: a no-op if the item is already gone,
/// matching the old ViewModel.</summary>
public sealed record RemoveWishlistItem(int ItemId) : ICommand;

public sealed class RemoveWishlistItemHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RemoveWishlistItem>
{
    public async Task HandleAsync(RemoveWishlistItem command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.WishlistItems.FindAsync([command.ItemId], ct);
        if (item is null) return; // already gone — idempotent

        db.WishlistItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }
}
