using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Backs the /publishers page. Lists all Publisher entities with their
// edition counts and supports rename / delete-unused / merge.
//
// Publishers are simpler than Authors — no canonical/alias model — so
// name variants like "Tor" vs "Tor Books" are reconciled via outright
// merge (target absorbs all editions, source deleted). Imprint-style
// aliasing (Pan → Macmillan) is a future extension.
//
// Each row can be expanded to show a drill-down of the editions this
// publisher covers. Editions load lazily on first expand and the cache is
// invalidated whenever a structural change (rename / merge / delete)
// could affect what's displayed.
public class PublisherListViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public bool Loading { get; private set; } = true;
    public List<PublisherRow> Publishers { get; private set; } = [];
    public string? SuccessMessage { get; set; }

    public HashSet<int> ExpandedPublisherIds { get; private set; } = [];
    public Dictionary<int, PublisherDetail> DetailByPublisherId { get; private set; } = [];

    public async Task LoadAsync()
    {
        Loading = true;
        await using var db = await dbFactory.CreateDbContextAsync();

        // OrderBy before Select so EF orders on the source entity column, not on
        // a property of a constructed PublisherRow. EF Core 10.x can't translate
        // OrderBy-on-record-projection when the record's constructor includes a
        // navigation aggregate (here `p.Editions.Count`) — it tries to invoke
        // the constructor inside the ORDER BY and fails. Anonymous types still
        // translate fine because EF maps property names back to source columns;
        // record positional constructors break that mapping.
        Publishers = await db.Publishers
            .OrderBy(p => p.Name)
            .Select(p => new PublisherRow(p.Id, p.Name, p.Editions.Count))
            .ToListAsync();

        Loading = false;
    }

    public async Task ToggleExpandAsync(int publisherId)
    {
        if (ExpandedPublisherIds.Remove(publisherId)) return;
        ExpandedPublisherIds.Add(publisherId);
        if (!DetailByPublisherId.ContainsKey(publisherId))
        {
            DetailByPublisherId[publisherId] = await LoadDetailAsync(publisherId);
        }
    }

    private async Task<PublisherDetail> LoadDetailAsync(int publisherId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var editions = await db.Editions
            .Where(e => e.PublisherId == publisherId)
            .Include(e => e.Book)
            .Include(e => e.Copies)
            .OrderBy(e => e.Book.Title)
            .ThenBy(e => e.DatePrinted)
            .Select(e => new EditionRow(
                e.Id,
                e.BookId,
                e.Book.Title,
                e.Isbn,
                e.Format,
                e.DatePrinted,
                e.CoverUrl,
                e.Copies.Count))
            .ToListAsync();

        return new PublisherDetail(editions);
    }

    public async Task RenameAsync(int publisherId, string newName)
    {
        var trimmed = newName.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Id == publisherId);
        if (publisher is null) return;

        // Publisher.Name has a unique index — guard against colliding with
        // another row explicitly so the user gets a helpful message instead
        // of a DB exception.
        var clash = await db.Publishers.AnyAsync(p => p.Id != publisherId && p.Name == trimmed);
        if (clash)
        {
            SuccessMessage = $"A publisher named \"{trimmed}\" already exists. Use the merge action to combine them.";
            return;
        }

        publisher.Name = trimmed;
        await db.SaveChangesAsync();
        SuccessMessage = $"Renamed to \"{trimmed}\".";
        InvalidateDetailsFor(publisherId);
        await LoadAsync();
    }

    public async Task DeleteUnusedAsync(int publisherId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var publisher = await db.Publishers
            .Include(p => p.Editions)
            .FirstOrDefaultAsync(p => p.Id == publisherId);
        if (publisher is null) return;

        // Edition.PublisherId has OnDelete.Restrict, so this would blow up
        // at the DB if attempted with editions attached. Guard at VM level
        // and surface a helpful message.
        if (publisher.Editions.Count > 0)
        {
            SuccessMessage = $"Can't delete \"{publisher.Name}\" — {publisher.Editions.Count} edition{(publisher.Editions.Count == 1 ? "" : "s")} still reference it. Merge it into another publisher instead.";
            return;
        }

        var name = publisher.Name;
        db.Publishers.Remove(publisher);
        await db.SaveChangesAsync();
        SuccessMessage = $"Deleted unused publisher \"{name}\".";
        InvalidateDetailsFor(publisherId);
        await LoadAsync();
    }

    public async Task MergeAsync(int sourceId, int targetId)
    {
        if (sourceId == targetId) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var source = await db.Publishers
            .Include(p => p.Editions)
            .FirstOrDefaultAsync(p => p.Id == sourceId);
        var target = await db.Publishers.FirstOrDefaultAsync(p => p.Id == targetId);
        if (source is null || target is null) return;

        var editionCount = source.Editions.Count;

        // Reassign then delete in a single transaction so we never leave a
        // half-merged state if SaveChanges fails partway. Matches the
        // pattern in AuthorMergeService.
        await using var tx = await db.Database.BeginTransactionAsync();

        foreach (var edition in source.Editions)
        {
            edition.PublisherId = targetId;
        }
        db.Publishers.Remove(source);

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        SuccessMessage = $"Merged \"{source.Name}\" into \"{target.Name}\" — {editionCount} edition{(editionCount == 1 ? "" : "s")} reassigned.";
        InvalidateDetailsFor(sourceId, targetId);
        await LoadAsync();
    }

    private void InvalidateDetailsFor(params int[] publisherIds)
    {
        foreach (var id in publisherIds) DetailByPublisherId.Remove(id);
    }

    public record PublisherRow(int Id, string Name, int EditionCount);

    public record PublisherDetail(IReadOnlyList<EditionRow> Editions)
    {
        public static PublisherDetail Empty => new([]);
    }

    public record EditionRow(
        int Id,
        int BookId,
        string BookTitle,
        string? Isbn,
        BookFormat Format,
        DateOnly? DatePrinted,
        string? CoverUrl,
        int CopyCount);
}
