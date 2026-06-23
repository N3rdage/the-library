using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Wishlist;

/// <summary>Adds an item to the wishlist — the unified path for both
/// search-and-add (a picked candidate, with cover + known ISBNs) and the manual
/// quick-add (typed title/author/priority + optional single ISBN). The ISBN
/// dual-write and author fallback live on the aggregate. Returns the new item's
/// display data, or null when the title is blank (a silent no-op matching the
/// old ViewModel).</summary>
public sealed record AddWishlistItem(
    string? Title,
    string? Author,
    WishlistPriority Priority,
    IReadOnlyList<string> Isbns,
    string? CoverUrl) : ICommand<WishlistItemResult?>;

/// <summary>The newly-added item's normalised display fields — enough for the
/// caller to render its row without a reload.</summary>
public sealed record WishlistItemResult(
    int Id, string Title, string Author, WishlistPriority Priority,
    string? PrimaryIsbn, string? CoverUrl);

public sealed class AddWishlistItemHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<AddWishlistItem, WishlistItemResult?>
{
    public async Task<WishlistItemResult?> HandleAsync(AddWishlistItem command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Title)) return null; // silent no-op (matches old VM)

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = WishlistItem.Create(
            command.Title, command.Author, command.Priority, command.Isbns ?? [], command.CoverUrl);
        db.WishlistItems.Add(item);
        await db.SaveChangesAsync(ct);

        return new WishlistItemResult(
            item.Id, item.Title, item.Author, item.Priority, item.Isbn, item.CoverUrl);
    }
}
