using BookTracker.Application.Duplicates;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Shared.Online;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

/// <summary>Does the library own the work behind an ISBN it may not hold? The
/// ISBN is matched exactly (pass it cleaned); the title and author are what a
/// provider said about it and drive the fallback match on another printing.
/// Returns null when the library owns neither the edition nor the work.</summary>
public sealed record FindOwnedWork(string Isbn, string? Title, string? Author) : IQuery<OwnedWorkMatch?>;

public sealed class FindOwnedWorkHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<FindOwnedWork, OwnedWorkMatch?>
{
    // The SQL narrowing (a surname or one title word) is loose on purpose so
    // that normalised comparison in memory can be exact; the cap keeps a
    // common surname or word from pulling the whole Works table.
    private const int CandidatePoolSize = 200;

    public async Task<OwnedWorkMatch?> HandleAsync(FindOwnedWork query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var edition = await db.Editions
            .AsNoTracking()
            .Where(e => e.Isbn == query.Isbn)
            .Select(e => new { e.BookId, e.Book.Title })
            .FirstOrDefaultAsync(ct);
        if (edition is not null) return new OwnedWorkMatch(edition.BookId, edition.Title, SameEdition: true);

        var wanted = DuplicateNormalization.Title(query.Title);
        if (wanted.Length == 0) return null;

        var works = db.Works.AsNoTracking();
        var surname = Surname(query.Author);
        if (surname is not null)
        {
            works = works.Where(w => w.WorkAuthors
                .Where(wa => wa.Role == AuthorRole.Author)
                .OrderBy(wa => wa.Order)
                .Select(wa => wa.Author.Name)
                .FirstOrDefault()!
                .Contains(surname));
        }
        else
        {
            var word = LongestWord(query.Title!);
            if (word is null) return null;
            works = works.Where(w => w.Title.Contains(word));
        }

        // Books is the skip navigation, so the soft-delete filter on Book
        // applies: a Work held only by tombstoned Books takes no pool slot.
        var candidates = await works
            .Where(w => w.Books.Any())
            .OrderBy(w => w.Id)
            .Take(CandidatePoolSize)
            .Select(w => new
            {
                w.Title,
                FirstBook = w.Books
                    .OrderBy(b => b.Id)
                    .Select(b => new { b.Id, b.Title })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var match = candidates.FirstOrDefault(c =>
            c.FirstBook is not null && DuplicateNormalization.Title(c.Title) == wanted);
        return match is null ? null : new OwnedWorkMatch(match.FirstBook!.Id, match.FirstBook.Title, SameEdition: false);
    }

    // Providers send either "Terry Pratchett" or the catalogue form
    // "Pratchett, Terry, 1948-" (Trove), so the surname is the part before a
    // comma when there is one, else the last word. Initials alone are too
    // loose to narrow on.
    private static string? Surname(string? author)
    {
        if (string.IsNullOrWhiteSpace(author)) return null;
        var comma = author.IndexOf(',');
        var name = comma >= 0 ? author[..comma] : author;
        return Words(name).LastOrDefault(w => w.Length > 1);
    }

    private static string? LongestWord(string title) =>
        Words(title).OrderByDescending(w => w.Length).FirstOrDefault();

    private static IEnumerable<string> Words(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (inWord && start < 0) start = i;
            else if (!inWord && start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
        }
    }
}
