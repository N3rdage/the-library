using BookTracker.Application.Formatting;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Loans;

// Books the lend dialog can add: non-deleted books whose title contains the
// query (case-insensitive, trimmed; a blank query returns nothing), each with
// its free copies (no open loan). Books with no free copy are left out. At most
// MaxResults books, ordered by title. Author is the lead Work's first
// Author-role contributor, as in GetOpenLoans. Copies follow GetBookDetail's
// order: edition by print date then id, copy by acquired date then id.
public sealed record SearchLendableBooks(string Query) : IQuery<IReadOnlyList<LendableBook>>
{
    public const int MaxResults = 20;
}

public record LendableBook(int BookId, string Title, string? Author, IReadOnlyList<LendableCopy> Copies);

public record LendableCopy(int CopyId, string EditionLabel, BookCondition Condition);

public sealed class SearchLendableBooksHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<SearchLendableBooks, IReadOnlyList<LendableBook>>
{
    public async Task<IReadOnlyList<LendableBook>> HandleAsync(SearchLendableBooks query, CancellationToken ct = default)
    {
        var term = query.Query?.Trim();
        if (string.IsNullOrEmpty(term)) return [];
        var lowered = term.ToLower();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var books = await db.Books
            .AsNoTracking()
            .Where(b => b.Title.ToLower().Contains(lowered)
                && b.Editions.Any(e => e.Copies.Any(c => !c.Loans.Any(l => l.ReturnedOn == null))))
            .OrderBy(b => b.Title)
            .ThenBy(b => b.Id)
            .Take(SearchLendableBooks.MaxResults)
            .Select(b => new
            {
                b.Id,
                b.Title,
                Author = b.BookWorks
                    .OrderBy(bw => bw.Order)
                    .ThenBy(bw => bw.WorkId)
                    .Select(bw => bw.Work.WorkAuthors
                        .Where(wa => wa.Role == AuthorRole.Author)
                        .OrderBy(wa => wa.Order)
                        .Select(wa => wa.Author.Name)
                        .FirstOrDefault())
                    .FirstOrDefault(),
                Editions = b.Editions.Select(e => new
                {
                    e.Id,
                    e.Format,
                    e.Isbn,
                    e.DatePrinted,
                    Copies = e.Copies
                        .Where(c => !c.Loans.Any(l => l.ReturnedOn == null))
                        .Select(c => new { c.Id, c.Condition, c.DateAcquired })
                        .ToList(),
                }).ToList(),
            })
            .ToListAsync(ct);

        return books
            .Select(b => new LendableBook(
                b.Id,
                b.Title,
                b.Author,
                b.Editions
                    .OrderBy(e => e.DatePrinted ?? DateOnly.MaxValue)
                    .ThenBy(e => e.Id)
                    .SelectMany(e => e.Copies
                        .OrderBy(c => c.DateAcquired ?? DateTime.MaxValue)
                        .ThenBy(c => c.Id)
                        .Select(c => new LendableCopy(c.Id, EditionLabels.For(e.Format, e.Isbn), c.Condition)))
                    .ToList()))
            .ToList();
    }
}
