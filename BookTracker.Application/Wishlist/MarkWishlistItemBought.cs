using BookTracker.Application.Authors;
using BookTracker.Application.Books;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Wishlist;

/// <summary>Promotes a wishlist item into the owned library: creates a Book (with
/// the "follow-up" tag and a single Work authored from the item) plus, when the
/// item carries an ISBN, an Edition + one Copy with default format/condition;
/// then removes the wishlist item. Returns the new Book's id, or null if the item
/// is already gone — loading it first (rather than trusting the caller's row)
/// means a double-click can't mint a duplicate book.</summary>
public sealed record MarkWishlistItemBought(int ItemId) : ICommand<int?>;

public sealed class MarkWishlistItemBoughtHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<MarkWishlistItemBought, int?>
{
    public async Task<int?> HandleAsync(MarkWishlistItemBought command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var item = await db.WishlistItems.FindAsync([command.ItemId], ct);
        if (item is null) return null; // gone between click + save — no duplicate book

        // Find-or-create the shared "follow-up" tag (a flat lookup, C9) via the
        // one shared resolver — same seam BookDetail's tag writes use (TD-15).
        var followUpTag = await TagResolver.FindOrCreateAsync("follow-up", db, ct);

        var author = await AuthorResolver.FindOrCreateAsync(item.Author, db, ct);
        var book = new Book { Title = item.Title, Tags = [followUpTag] };
        var work = Work.Create(book, item.Title, null, null, DatePrecision.Day, [author]);
        db.Books.Add(book);
        db.Works.Add(work); // work.Books holds the link; book.Works isn't populated by the factory

        // Route the Edition through the aggregate factory so it owns its first
        // Copy and the ISBN is normalised — same seam every other add path uses.
        if (!string.IsNullOrWhiteSpace(item.Isbn))
            book.AddEdition(item.Isbn, BookFormat.TradePaperback, datePrinted: null,
                DatePrecision.Day, coverUrl: null, publisher: null, BookCondition.Good);

        db.WishlistItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return book.Id;
    }
}
