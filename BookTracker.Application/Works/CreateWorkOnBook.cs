using BookTracker.Application.Authors;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

/// <summary>Creates a new Work and attaches it to a Book as its first appearance.
/// Returns the new Work's id, or null when the book is gone or no contributor of
/// any role was supplied (a silent no-op, matching the dialog guard).</summary>
public sealed record CreateWorkOnBook(
    int BookId,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> AuthorNames,
    IReadOnlyList<ContributorInput> Contributors,
    DateOnly? FirstPublished,
    DatePrecision Precision,
    IReadOnlyList<int> GenreIds) : ICommand<int?>;

public sealed class CreateWorkOnBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<CreateWorkOnBook, int?>
{
    public async Task<int?> HandleAsync(CreateWorkOnBook command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct);
        if (book is null) return null;

        var authors = await AuthorResolver.FindOrCreateAllAsync(command.AuthorNames, db, ct);
        var contributors = new List<(Author Person, AuthorRole Role)>();
        foreach (var c in command.Contributors)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) continue;
            contributors.Add((await AuthorResolver.FindOrCreateAsync(c.Name, db, ct), c.Role));
        }
        if (authors.Count == 0 && contributors.Count == 0) return null; // nothing to credit — soft no-op

        var genres = await ResolveGenresAsync(db, command.GenreIds, ct);
        var work = Work.Create(book, command.Title, command.Subtitle,
            command.FirstPublished, command.Precision, authors, contributors);
        work.SetGenres(genres);
        db.Works.Add(work);
        await db.SaveChangesAsync(ct);
        return work.Id;
    }

    internal static async Task<List<Genre>> ResolveGenresAsync(
        BookTrackerDbContext db, IReadOnlyList<int> genreIds, CancellationToken ct) =>
        genreIds.Count == 0
            ? []
            : await db.Genres.Where(g => genreIds.Contains(g.Id)).ToListAsync(ct);
}
