using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Soft-deletes a Book. The Book row survives as a tombstone
/// (DeletedAt stamped, hidden by the global query filter) so the catalog
/// snapshot can emit it in deletedIds[]; its Editions (and Copies via cascade)
/// are hard-removed, and its Tag joins cleared. Works are detached via the
/// ref-count lifecycle — a Work left in no other book is orphaned and deleted,
/// a Work still on another book survives detached.</summary>
public sealed record DeleteBook(int BookId) : ICommand;

public sealed class DeleteBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<DeleteBook>
{
    public async Task HandleAsync(DeleteBook command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books
            .Include(b => b.Editions)
            .Include(b => b.Tags)
            // BookWorks is the canonical membership; ThenInclude each Work's own
            // BookWorks so RemoveFrom can ref-count it across its other books.
            .Include(b => b.BookWorks).ThenInclude(bw => bw.Work).ThenInclude(w => w.BookWorks)
            .FirstOrDefaultAsync(b => b.Id == command.BookId, ct)
            ?? throw new NotFoundException($"Book {command.BookId} not found.");

        // Detach the book's Works; any Work now in no book (ref count 0) is
        // orphaned and deleted — the same lifecycle RemoveWorkFromBook uses, so
        // deleting a book takes its exclusive Works with it but leaves shared ones.
        var orphaned = new List<Work>();
        foreach (var work in book.BookWorks.Select(bw => bw.Work).ToList())
            if (work.RemoveFrom(book)) orphaned.Add(work);
        if (orphaned.Count > 0) db.Works.RemoveRange(orphaned);

        // SoftDelete() orphan-removes the Editions (Copies cascade), clears the
        // Tag joins, and stamps the tombstone.
        book.SoftDelete();

        await db.SaveChangesAsync(ct);
    }
}
