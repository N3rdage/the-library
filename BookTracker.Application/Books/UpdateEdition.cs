using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Updates an existing Edition's fields. Publisher is resolved
/// find-or-create by name.</summary>
public sealed record UpdateEdition(
    int EditionId,
    string? Isbn,
    BookFormat Format,
    DateOnly? DatePrinted,
    DatePrecision DatePrintedPrecision,
    string? PublisherName,
    string? CoverUrl);

// Loads the Edition leaf directly rather than through the Book root: editing
// edition fields carries no cross-entity invariant, and the interceptor still
// bumps the parent Book's UpdatedAt on save. (Aggregate-root purists would
// load the Book and navigate; the pilot doesn't pay for that here — revisit
// at the post-PR1 gate.)
public sealed class UpdateEditionHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public async Task HandleAsync(UpdateEdition command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var edition = await db.Editions.FindAsync([command.EditionId], ct)
            ?? throw new NotFoundException($"Edition {command.EditionId} not found.");

        var publisher = await PublisherResolver.ResolveAsync(db, command.PublisherName, ct);
        edition.UpdateDetails(
            command.Isbn,
            command.Format,
            command.DatePrinted,
            command.DatePrintedPrecision,
            command.CoverUrl,
            publisher);

        await db.SaveChangesAsync(ct);
    }
}
