using BookTracker.Application.Formatting;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

// Read-model for /authors/{id}. Relocated from AuthorDetailViewModel's inline
// DbContext reads in PR6b-2. Returns null when the author doesn't exist (the VM
// maps that to NotFound). Bundles the header, the Works/Books drill-down, and
// the canonical-candidate list for the "mark as alias of…" dropdown so the page
// loads in a single dispatch.
public sealed record GetAuthorDetail(int AuthorId) : IQuery<AuthorDetailResult?>;

public record AuthorDetailResult(
    AuthorHeader Header,
    AuthorDetail Detail,
    IReadOnlyList<CanonicalCandidate> CanonicalCandidates);

public record AuthorHeader(int Id, string Name, int? CanonicalAuthorId, string? CanonicalName);

public record AuthorDetail(
    IReadOnlyList<WorkRow> Works,
    IReadOnlyList<BookSummaryRow> Books,
    IReadOnlyList<string> AliasNames)
{
    public static AuthorDetail Empty => new([], [], []);
}

public record WorkRow(
    int Id,
    string Title,
    string? Subtitle,
    string? WrittenAs,
    string FirstPublishedDisplay,
    string? SeriesName,
    SeriesType? SeriesType,
    string? SeriesOrderLabel,
    IReadOnlyList<BookRef> InBooks);

public record BookSummaryRow(
    int Id,
    string Title,
    string? CoverUrl,
    int EditionCount,
    int CopyCount);

public record BookRef(int Id, string Title);

public record CanonicalCandidate(int Id, string Name);

public sealed class GetAuthorDetailHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetAuthorDetail, AuthorDetailResult?>
{
    public async Task<AuthorDetailResult?> HandleAsync(GetAuthorDetail query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var author = await db.Authors
            .AsNoTracking()
            .Include(a => a.CanonicalAuthor)
            .Include(a => a.Aliases)
            .FirstOrDefaultAsync(a => a.Id == query.AuthorId, ct);

        if (author is null) return null;

        var header = new AuthorHeader(
            author.Id,
            author.Name,
            author.CanonicalAuthorId,
            author.CanonicalAuthor?.Name);

        var detail = await LoadDetailAsync(db, author, ct);

        // Canonical-candidate list for the "mark as alias of" dropdown — every
        // canonical author except this one. Avoid alias-of-alias chains (the
        // command's re-root rule also handles it, but this trims the dropdown).
        var candidates = await db.Authors
            .AsNoTracking()
            .Where(a => a.CanonicalAuthorId == null && a.Id != query.AuthorId)
            .OrderBy(a => a.Name)
            .Select(a => new CanonicalCandidate(a.Id, a.Name))
            .ToListAsync(ct);

        return new AuthorDetailResult(header, detail, candidates);
    }

    private static async Task<AuthorDetail> LoadDetailAsync(BookTrackerDbContext db, Author author, CancellationToken ct)
    {
        bool isCanonical = author.CanonicalAuthorId is null;

        var ids = new List<int> { author.Id };
        if (isCanonical)
        {
            ids.AddRange(author.Aliases.Select(a => a.Id));
        }

        // Default drilldown: only Works where this author is in an
        // Author role. Editor/translator/illustrator contributions can be
        // shown via a future opt-in toggle.
        var works = await db.Works
            .AsNoTracking()
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(w => w.Books)
            .Include(w => w.Series)
            .Where(w => w.WorkAuthors.Any(wa => ids.Contains(wa.AuthorId) && wa.Role == AuthorRole.Author))
            .OrderBy(w => w.SeriesId == null)
            .ThenBy(w => w.Series!.Name)
            .ThenBy(w => w.SeriesOrder ?? int.MaxValue)
            .ThenBy(w => w.Title)
            .ToListAsync(ct);

        var workRows = works.Select(w => new WorkRow(
            w.Id,
            w.Title,
            w.Subtitle,
            // "Written as" tag only on canonical drill-downs; alias rows always
            // are themselves so the label would be redundant.
            isCanonical
                ? w.WorkAuthors
                    .Where(wa => wa.Role == AuthorRole.Author)
                    .OrderBy(wa => wa.Order)
                    .Select(wa => wa.Author)
                    .FirstOrDefault() is { } leadAuthor && leadAuthor.Id != author.Id
                        ? leadAuthor.Name
                        : null
                : null,
            PartialDateParser.Format(w.FirstPublishedDate, w.FirstPublishedDatePrecision),
            w.Series?.Name,
            w.Series?.Type,
            SeriesOrderParser.Format(w.SeriesOrder, w.SeriesOrderDisplay),
            w.Books.Select(b => new BookRef(b.Id, b.Title)).ToList()
        )).ToList();

        var bookIds = works.SelectMany(w => w.Books).Select(b => b.Id).Distinct().ToList();
        var books = await db.Books
            .AsNoTracking()
            .Where(b => bookIds.Contains(b.Id))
            .Include(b => b.Editions).ThenInclude(e => e.Copies)
            .OrderBy(b => b.Title)
            .ToListAsync(ct);

        var bookRows = books.Select(b => new BookSummaryRow(
            b.Id,
            b.Title,
            b.DefaultCoverArtUrl,
            b.Editions.Count,
            b.Editions.Sum(e => e.Copies.Count)
        )).ToList();

        var aliasNames = isCanonical
            ? author.Aliases.Select(a => a.Name).OrderBy(n => n).ToList()
            : new List<string>();

        return new AuthorDetail(workRows, bookRows, aliasNames);
    }
}
