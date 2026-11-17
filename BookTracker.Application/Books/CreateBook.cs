using BookTracker.Application.Series;
using BookTracker.Application.Works;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
// Authors/ also declares a WorkRow (an author-detail read model); alias the
// resolver rather than import that namespace.
using AuthorResolver = BookTracker.Application.Authors.AuthorResolver;

namespace BookTracker.Application.Books;

/// <summary>Creates a Book from the Add page in one save: the book, its works
/// (a single new Work, or a collection of new and existing Works), its first
/// Edition with one Copy, and its series membership. Returns the new Book's id.
/// Throws <see cref="InvalidOperationException"/> with a user-facing message when
/// the works carry no usable contributor or no usable collection row.</summary>
public sealed record CreateBook(
    NewBook Book,
    NewBookWorks Works,
    NewEdition Edition,
    BookCondition CopyCondition,
    SeriesChoice Series) : ICommand<int>;

/// <summary>The Book-level fields captured on the Add page.</summary>
public sealed record NewBook(
    string Title,
    BookCategory Category,
    BookStatus Status,
    int Rating,
    string? Notes,
    string? CoverUrl);

/// <summary>The works a new Book holds: <see cref="SingleWork"/> or
/// <see cref="WorkCollection"/>.</summary>
public abstract record NewBookWorks;

/// <summary>The one Work of a single-work book. Dates arrive already parsed (the
/// VM owns the free-text parsing).</summary>
public sealed record SingleWork(
    string Title,
    string? Subtitle,
    DateOnly? FirstPublished,
    DatePrecision Precision,
    IReadOnlyList<string> Authors,
    IReadOnlyList<ContributorInput> Contributors,
    IReadOnlyList<int> GenreIds) : NewBookWorks;

/// <summary>The rows of a collection, each a new Work or an existing one to
/// attach. Single-author / single-genre modes apply the shared lists to every
/// new row.</summary>
public sealed record WorkCollection(
    IReadOnlyList<WorkRow> Rows,
    bool SingleAuthor,
    bool SingleGenre,
    IReadOnlyList<string> SharedAuthors,
    IReadOnlyList<int> SharedGenreIds) : NewBookWorks;

/// <summary>The first Edition of the new Book. The publisher is resolved
/// find-or-create by name.</summary>
public sealed record NewEdition(
    string? Isbn,
    BookFormat Format,
    int? EditionNumber,
    DateOnly? DatePrinted,
    DatePrecision DatePrintedPrecision,
    string? PublisherName,
    string? CoverUrl);

/// <summary>The series the book joins: a resolved id, else a name to
/// find-or-create, with the free-text order label. Both blank means none.</summary>
public sealed record SeriesChoice(int? SeriesId, string? Name, string? OrderLabel);

public sealed class CreateBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<CreateBook, int>
{
    public async Task<int> HandleAsync(CreateBook command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var publisher = await PublisherResolver.ResolveAsync(db, command.Edition.PublisherName, ct);

        // Resolve every author, genre and existing work before the Book exists,
        // so a validation throw leaves nothing half-built; each step then attaches
        // its work to the book in capture order.
        var attachSteps = command.Works switch
        {
            SingleWork single => await PlanSingleWorkAsync(db, single, ct),
            WorkCollection collection => await PlanCollectionAsync(db, collection, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(command), "Unknown works input."),
        };

        var input = command.Book;
        var book = Book.Create(input.Title, input.Category, input.Status, input.Rating, input.Notes, input.CoverUrl);

        var edition = command.Edition;
        book.AddEdition(
            edition.Isbn,
            edition.Format,
            edition.EditionNumber,
            edition.DatePrinted,
            edition.DatePrintedPrecision,
            edition.CoverUrl,
            publisher,
            command.CopyCondition);

        foreach (var attach in attachSteps)
            attach(book);

        await SeriesResolver.AttachToBookAsync(
            db, book, command.Series.SeriesId, command.Series.Name, command.Series.OrderLabel, ct);

        db.Books.Add(book);
        await db.SaveChangesAsync(ct);
        return book.Id;
    }

    private static async Task<List<Action<Book>>> PlanSingleWorkAsync(
        BookTrackerDbContext db, SingleWork work, CancellationToken ct)
    {
        var authors = await AuthorResolver.FindOrCreateAllAsync(work.Authors, db, ct);

        // Non-Author contributors resolve one by one, each paired with its role.
        // Blank-name rows are skipped (the picker permits an empty pending row).
        var contributors = new List<(Author Person, AuthorRole Role)>();
        foreach (var entry in work.Contributors)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)) continue;
            contributors.Add((await AuthorResolver.FindOrCreateAsync(entry.Name, db, ct), entry.Role));
        }
        if (authors.Count == 0 && contributors.Count == 0)
            throw new InvalidOperationException(
                "At least one contributor (author, editor, or other role) is required to save a Work.");

        var genres = await CreateWorkOnBookHandler.ResolveGenresAsync(db, work.GenreIds, ct);

        return
        [
            book => Work.Create(book, work.Title, work.Subtitle, work.FirstPublished, work.Precision, authors, contributors)
                .SetGenres(genres),
        ];
    }

    private static async Task<List<Action<Book>>> PlanCollectionAsync(
        BookTrackerDbContext db, WorkCollection collection, CancellationToken ct)
    {
        IReadOnlyList<string> AuthorsFor(WorkRow row) => collection.SingleAuthor ? collection.SharedAuthors : row.Authors;
        IReadOnlyList<int> GenresFor(WorkRow row) => collection.SingleGenre ? collection.SharedGenreIds : row.GenreIds;

        // Attach-existing rows always count; a new row needs a title and an
        // author entry (rows without them are dropped silently). The kept rows
        // stay in on-screen order, which becomes the book's work order.
        var orderedRows = collection.Rows
            .Where(r => r.AttachedWorkId is not null
                        || (!string.IsNullOrWhiteSpace(r.Title) && AuthorsFor(r).Count > 0))
            .ToList();
        if (orderedRows.Count == 0)
        {
            throw new InvalidOperationException(collection.SingleAuthor
                ? "A collection in Single-Author mode needs at least one work with a title, plus at least one shared author. (Or attach an existing work.)"
                : "A collection must contain at least one work with a title and an author. (Or attach an existing work.)");
        }

        var newRows = orderedRows.Where(r => r.AttachedWorkId is null).ToList();
        var attachIds = orderedRows
            .Where(r => r.AttachedWorkId is int)
            .Select(r => r.AttachedWorkId!.Value)
            .Distinct()
            .ToList();

        // One find-or-create pass over every author and contributor name, so a
        // person named on several rows (or as Author on one and Editor on
        // another) resolves to a single Author entity.
        var allNames = collection.SingleAuthor ? collection.SharedAuthors : newRows.SelectMany(r => r.Authors);
        var contributorNames = newRows.SelectMany(r => r.Contributors.Select(c => c.Name));
        var allAuthors = await AuthorResolver.FindOrCreateAllAsync(allNames.Concat(contributorNames), db, ct);
        var byName = allAuthors.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        var allGenreIds = (collection.SingleGenre ? collection.SharedGenreIds : newRows.SelectMany(r => r.GenreIds))
            .Distinct()
            .ToList();
        var genres = await CreateWorkOnBookHandler.ResolveGenresAsync(db, allGenreIds, ct);
        var genresById = genres.ToDictionary(g => g.Id);

        // An existing work deleted between the pick and the save is dropped
        // rather than failing the whole save.
        var attachedWorks = attachIds.Count == 0
            ? []
            : await db.Works.Where(w => attachIds.Contains(w.Id)).ToListAsync(ct);
        var attachedById = attachedWorks.ToDictionary(w => w.Id);

        var steps = new List<Action<Book>>(orderedRows.Count);
        foreach (var row in orderedRows)
        {
            if (row.AttachedWorkId is int existingId)
            {
                if (attachedById.TryGetValue(existingId, out var existing))
                    steps.Add(book => book.AttachWork(existing));
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
            {
                throw new InvalidOperationException(
                    $"Each work in the collection needs at least one contributor (work \"{row.Title}\" had none).");
            }
            var rowGenres = GenresFor(row)
                .Distinct()
                .Where(genresById.ContainsKey)
                .Select(id => genresById[id])
                .ToList();

            steps.Add(book => Work.Create(book, row.Title!, row.Subtitle, row.FirstPublished, row.Precision, rowAuthors, rowContributors)
                .SetGenres(rowGenres));
        }
        return steps;
    }
}
