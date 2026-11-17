using BookTracker.Application.Formatting;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Finds the Edition stored under an exact ISBN, for the Add page's
/// "you already own this book" banner and bulk add's owned-book rows. The caller
/// passes the ISBN in the form it matches against. Returns null when no edition
/// carries it.</summary>
public sealed record FindEditionByIsbn(string Isbn) : IQuery<ExistingBookMatch?>;

/// <summary>The owned book an ISBN lookup hit: the book and edition to add a copy
/// to, the title, the Author-role names across its works (comma-joined, lead
/// author first within each work, each name once) and the edition's copy count;
/// then the lead work's subtitle and full authorship line (null when the book has
/// no work), the cover (the edition's, else the book's default), the publisher
/// name, the print date and the format.</summary>
public sealed record ExistingBookMatch(
    int BookId,
    int EditionId,
    string Title,
    string Authors,
    int CopyCount,
    string? LeadWorkSubtitle,
    string? LeadWorkAuthorship,
    string? CoverUrl,
    string? Publisher,
    DateOnly? DatePrinted,
    DatePrecision DatePrintedPrecision,
    BookFormat Format);

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
                    bw.Work.Subtitle,
                    Contributors = bw.Work.WorkAuthors
                        .Select(wa => new { wa.Order, wa.Role, wa.Author.Name })
                        .ToList(),
                }).ToList(),
                CopyCount = e.Copies.Count,
                e.CoverUrl,
                e.Book.DefaultCoverArtUrl,
                Publisher = e.Publisher == null ? null : e.Publisher.Name,
                e.DatePrinted,
                e.DatePrintedPrecision,
                e.Format,
            })
            .FirstOrDefaultAsync(ct);

        if (edition is null) return null;

        var works = edition.Works
            .OrderBy(w => w.Order)
            .ThenBy(w => w.WorkId)
            .ToList();

        var authors = works
            .SelectMany(w => w.Contributors
                .Where(c => c.Role == AuthorRole.Author)
                .OrderBy(c => c.Order)
                .Select(c => c.Name))
            .Distinct();

        var lead = works.FirstOrDefault();

        return new ExistingBookMatch(
            edition.BookId,
            edition.Id,
            edition.Title,
            string.Join(", ", authors),
            edition.CopyCount,
            lead?.Subtitle,
            lead is null ? null : WorkAuthorshipFormatter.Display(lead.Contributors.Select(c => (c.Name, c.Role, c.Order))),
            edition.CoverUrl ?? edition.DefaultCoverArtUrl,
            edition.Publisher,
            edition.DatePrinted,
            edition.DatePrintedPrecision,
            edition.Format);
    }
}
