using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Detaches a tag from a book, leaving the Tag row intact (other books may
// reference it). Relocated from BookDetailViewModel.RemoveTagAsync in PR6b-3.
// No-op if the book — or the tag-on-book — is already gone.
public sealed record RemoveTagFromBook(int BookId, int TagId) : ICommand;

public sealed class RemoveTagFromBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RemoveTagFromBook>
{
    public async Task HandleAsync(RemoveTagFromBook command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.Include(b => b.Tags).FirstOrDefaultAsync(b => b.Id == command.BookId, ct);
        if (book is null) return;

        var tag = book.Tags.FirstOrDefault(t => t.Id == command.TagId);
        if (tag is not null)
        {
            book.Tags.Remove(tag);
            await db.SaveChangesAsync(ct);
        }
    }
}
