using BookTracker.Application.Wishlist;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the AI assistant's wishlist dedupe query.
[Trait("Category", TestCategories.Integration)]
public class GetWishlistItemExistsHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task GetWishlistItemExists_matchesOnTitleAndAuthor()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.WishlistItems.Add(WishlistItem.Create("Hyperion", "Dan Simmons", WishlistPriority.Medium, [], null));
            await db.SaveChangesAsync();
        }
        var handler = new GetWishlistItemExistsHandler(_factory);

        Assert.True(await handler.HandleAsync(new GetWishlistItemExists("Hyperion", "Dan Simmons")));
        Assert.False(await handler.HandleAsync(new GetWishlistItemExists("Hyperion", "Someone Else")));
        Assert.False(await handler.HandleAsync(new GetWishlistItemExists("Endymion", "Dan Simmons")));
    }
}
