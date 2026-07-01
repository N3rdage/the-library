using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

/// <summary>Attaches an existing Work to a Book it also appears in. Returns the
/// Work's title on success, or null when the book/work is gone or the Work is
/// already on this book (soft outcomes — the search filter normally prevents the
/// last case, but stale dialog state can leak through).</summary>
public sealed record AttachWorkToBook(int BookId, int WorkId) : ICommand<string?>;

public sealed class AttachWorkToBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<AttachWorkToBook, string?>
{
    public async Task<string?> HandleAsync(AttachWorkToBook command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Include BookWorks so AppearsIn → Book.AttachWork appends the work to
        // the end of the book's existing order rather than defaulting to Order 0.
        var book = await db.Books
            .Include(b => b.BookWorks)
            .FirstOrDefaultAsync(b => b.Id == command.BookId, ct);
        if (book is null) return null;

        var work = await db.Works.FirstOrDefaultAsync(w => w.Id == command.WorkId, ct);
        if (work is null) return null;

        if (!work.AppearsIn(book)) return null; // already on this book
        await db.SaveChangesAsync(ct);
        return work.Title;
    }
}
