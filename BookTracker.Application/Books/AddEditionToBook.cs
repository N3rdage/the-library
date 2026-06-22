using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Adds a new Edition (with its first Copy) to a Book. The publisher
/// is resolved find-or-create by name. Returns the new Edition's id.</summary>
public sealed record AddEditionToBook(
    int BookId,
    string? Isbn,
    BookFormat Format,
    DateOnly? DatePrinted,
    DatePrecision DatePrintedPrecision,
    string? PublisherName,
    string? CoverUrl,
    BookCondition FirstCopyCondition) : ICommand<int>;

public sealed class AddEditionToBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<AddEditionToBook, int>
{
    public async Task<int> HandleAsync(AddEditionToBook command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books
            .Include(b => b.Editions)
            .FirstOrDefaultAsync(b => b.Id == command.BookId, ct)
            ?? throw new NotFoundException($"Book {command.BookId} not found.");

        var publisher = await PublisherResolver.ResolveAsync(db, command.PublisherName, ct);
        var edition = book.AddEdition(
            command.Isbn,
            command.Format,
            command.DatePrinted,
            command.DatePrintedPrecision,
            command.CoverUrl,
            publisher,
            command.FirstCopyCondition);

        await db.SaveChangesAsync(ct);
        return edition.Id;
    }
}
