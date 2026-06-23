using BookTracker.Application.Authors;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

/// <summary>Attaches a batch of new + existing Works to a Book in one save — the
/// compendium flow (a captured single-Work book turns out to be an N-story
/// anthology). Single-author / single-genre modes apply a shared author/genre
/// set to every new row. Returns the count of Works actually attached (already-
/// attached existing rows are skipped silently). Throws
/// <see cref="InvalidOperationException"/> with a user-facing message when there
/// are no usable rows, or a new row carries a title but no contributor.</summary>
public sealed record AttachWorksToBook(
    int BookId,
    IReadOnlyList<WorkRow> Rows,
    bool SingleAuthor,
    bool SingleGenre,
    IReadOnlyList<string> SharedAuthors,
    IReadOnlyList<int> SharedGenreIds) : ICommand<int>;

public sealed class AttachWorksToBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<AttachWorksToBook, int>
{
    public async Task<int> HandleAsync(AttachWorksToBook command, CancellationToken ct = default)
    {
        IReadOnlyList<string> AuthorsFor(WorkRow row) => command.SingleAuthor ? command.SharedAuthors : row.Authors;
        IReadOnlyList<int> GenresFor(WorkRow row) => command.SingleGenre ? command.SharedGenreIds : row.GenreIds;

        // Filter to rows that carry usable content. A row with a title but no
        // contributors goes through to the per-row throw below (named so the
        // user sees which row is the problem).
        var orderedRows = command.Rows
            .Where(r => r.AttachedWorkId is not null || !string.IsNullOrWhiteSpace(r.Title))
            .ToList();
        if (orderedRows.Count == 0)
            throw new InvalidOperationException(
                "Add at least one work — type a title for a new work, or pick an existing one from the dropdown.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.Include(b => b.Works).FirstOrDefaultAsync(b => b.Id == command.BookId, ct);
        if (book is null) return 0;

        var alreadyAttachedIds = book.Works.Select(w => w.Id).ToHashSet();
        var newRows = orderedRows.Where(r => r.AttachedWorkId is null).ToList();
        var attachIds = orderedRows
            .Where(r => r.AttachedWorkId is int)
            .Select(r => r.AttachedWorkId!.Value)
            .Distinct()
            .ToList();

        // Single-pass name resolution so a person credited on multiple rows
        // resolves to one Author entity.
        var allNames = (command.SingleAuthor ? command.SharedAuthors : newRows.SelectMany(r => r.Authors))
            .Concat(newRows.SelectMany(r => r.Contributors.Select(c => c.Name)));
        var allAuthors = await AuthorResolver.FindOrCreateAllAsync(allNames, db, ct);
        var byName = allAuthors.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        var allGenreIds = (command.SingleGenre ? command.SharedGenreIds : newRows.SelectMany(r => r.GenreIds))
            .Distinct()
            .ToList();
        var genres = await CreateWorkOnBookHandler.ResolveGenresAsync(db, allGenreIds, ct);
        var genresById = genres.ToDictionary(g => g.Id);

        var attachedWorks = attachIds.Count == 0
            ? []
            : await db.Works.Where(w => attachIds.Contains(w.Id)).ToListAsync(ct);
        var attachedById = attachedWorks.ToDictionary(w => w.Id);

        var attachedCount = 0;
        foreach (var row in orderedRows)
        {
            if (row.AttachedWorkId is int existingId)
            {
                if (alreadyAttachedIds.Contains(existingId)) continue;       // already on this book — silent skip
                if (!attachedById.TryGetValue(existingId, out var existing)) continue; // deleted between pick + save
                existing.AppearsIn(book);   // Work owns the relationship (C11); dedup handled by alreadyAttachedIds
                alreadyAttachedIds.Add(existingId);
                attachedCount++;
                continue;
            }

            var rowAuthors = AuthorsFor(row)
                .Select(n => n?.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => byName[n!])
                .ToList();
            var rowContributors = row.Contributors
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) && byName.ContainsKey(c.Name.Trim()))
                .Select(c => (Person: byName[c.Name.Trim()], c.Role))
                .ToList();
            if (rowAuthors.Count == 0 && rowContributors.Count == 0)
                throw new InvalidOperationException(
                    $"Work \"{row.Title}\" needs at least one contributor (author, editor, or other role).");

            var rowGenres = GenresFor(row)
                .Distinct()
                .Where(genresById.ContainsKey)
                .Select(id => genresById[id])
                .ToList();

            var work = Work.Create(book, row.Title!, row.Subtitle, row.FirstPublished, row.Precision, rowAuthors, rowContributors);
            work.SetGenres(rowGenres);
            db.Works.Add(work);
            attachedCount++;
        }

        await db.SaveChangesAsync(ct);
        return attachedCount;
    }
}
