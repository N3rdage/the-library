using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Finds the Edition stored under an exact ISBN, for the Add page's
/// "you already own this book" banner. The caller passes the ISBN in the form
/// it matches against. Returns null when no edition carries it.</summary>
public sealed record FindEditionByIsbn(string Isbn) : IQuery<ExistingBookMatch?>;

/// <summary>The owned book an ISBN lookup hit: the book and edition to add a copy
/// to, the title, the Author-role names across its works (comma-joined, lead
/// author first within each work, each name once) and the edition's copy count.</summary>
public sealed record ExistingBookMatch(int BookId, int EditionId, string Title, string Authors, int CopyCount);

public sealed class FindEditionByIsbnHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<FindEditionByIsbn, ExistingBookMatch?>
{
    public async Task<ExistingBookMatch?> HandleAsync(FindEditionByIsbn query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var edition = await db.Editions
            .AsNoTracking()
            .Where(e => e.Isbn == query.Isbn)
            .Select(e => new
            {
                e.BookId,
                e.Id,
                e.Book.Title,
                Works = e.Book.BookWorks.Select(bw => new
                {
                    bw.Order,
                    bw.WorkId,
                    Authors = bw.Work.WorkAuthors
                        .Where(wa => wa.Role == AuthorRole.Author)
                        .Select(wa => new { wa.Order, wa.Author.Name })
                        .ToList(),
                }).ToList(),
                CopyCount = e.Copies.Count,
            })
            .FirstOrDefaultAsync(ct);

        if (edition is null) return null;

        var authors = edition.Works
            .OrderBy(w => w.Order)
            .ThenBy(w => w.WorkId)
            .SelectMany(w => w.Authors.OrderBy(a => a.Order).Select(a => a.Name))
            .Distinct();

        return new ExistingBookMatch(
            edition.BookId,
            edition.Id,
            edition.Title,
            string.Join(", ", authors),
            edition.CopyCount);
    }
}
