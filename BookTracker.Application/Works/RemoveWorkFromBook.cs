using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

/// <summary>Removes a Work from a Book. If the Work now appears in no books
/// (orphaned) it is deleted outright — the ref-count lifecycle the Work aggregate
/// owns. Returns the Work's title, or null when the book/work is gone or the Work
/// wasn't on this book.</summary>
public sealed record RemoveWorkFromBook(int BookId, int WorkId) : ICommand<string?>;

public sealed class RemoveWorkFromBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RemoveWorkFromBook, string?>
{
    public async Task<string?> HandleAsync(RemoveWorkFromBook command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct);
        if (book is null) return null;

        var work = await db.Works.Include(w => w.Books).FirstOrDefaultAsync(w => w.Id == command.WorkId, ct);
        if (work is null) return null;
        if (!work.Books.Any(b => b.Id == command.BookId)) return null; // not on this book

        var title = work.Title;
        if (work.RemoveFrom(book))      // last book removed → orphaned
            db.Works.Remove(work);      // EF cascades WorkAuthors + clears Genre/Book joins
        await db.SaveChangesAsync(ct);
        return title;
    }
}
