using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using SeriesAggregate = BookTracker.Data.Models.Series;

namespace BookTracker.Application.Series;

/// <summary>Creates a Series (or Collection) from an AI grouping suggestion and
/// assigns the books whose title matches one of <see cref="BookTitles"/>,
/// numbering them from 1 and skipping any book already in a series. The
/// series' display author is set when every matched book's works share one
/// lead author. A no-op (returns null) when a series with that (trimmed) name
/// exists; otherwise returns the new series' id. Throws
/// <see cref="DomainRuleException"/> on a blank name, or when a duplicate name
/// reaches the unique index past the pre-check. One save, so the series and its
/// assignments land together. Relocated from
/// AIAssistantViewModel.CreateCollectionFromSuggestionAsync.</summary>
public sealed record CreateSeriesFromSuggestion(
    string Name,
    SeriesType Type,
    string? Description,
    IReadOnlyList<string> BookTitles) : ICommand<int?>;

public sealed class CreateSeriesFromSuggestionHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<CreateSeriesFromSuggestion, int?>
{
    public async Task<int?> HandleAsync(CreateSeriesFromSuggestion command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Compared trimmed, as the aggregate stores it. A blank name falls
        // through to Series.Create, which throws DomainRuleException.
        var trimmedName = command.Name.Trim();
        if (trimmedName.Length > 0 && await db.Series.AnyAsync(s => s.Name == trimmedName, ct)) return null;

        var titles = command.BookTitles.ToList();
        var matchedBooks = await db.Books
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Where(b => titles.Contains(b.Title))
            .ToListAsync(ct);

        var authorNames = matchedBooks
            .SelectMany(b => b.Works.SelectMany(w => w.WorkAuthors
                .Where(wa => wa.Role == AuthorRole.Author)
                .OrderBy(wa => wa.Order)
                .Select(wa => wa.Author.Name)))
            .Distinct()
            .ToList();

        var series = SeriesAggregate.Create(
            command.Name,
            authorNames.Count == 1 ? authorNames[0] : null,
            command.Type,
            expectedCount: null,
            command.Description);
        db.Series.Add(series);

        // A brand-new series has no members, so numbering starts after order 0.
        // Series membership is per-Book (the book is installment N); a book
        // already in another series is left where it is.
        var order = 1;
        foreach (var book in matchedBooks)
        {
            if (book.SeriesId is not null) continue;
            book.AssignToSeries(series, order++, orderDisplay: null);
        }

        // A clash that slips past the pre-check (a concurrent create, or a
        // collation difference) hits the unique index; the guard turns that into
        // DomainRuleException instead of a raw DbUpdateException.
        await SeriesNameGuard.SaveTranslatingDuplicateAsync(db, trimmedName, excludeId: null, ct);
        return series.Id;
    }
}
