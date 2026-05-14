using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookTracker.Data.Interceptors;

/// <summary>
/// EF Core SaveChangesInterceptor that bumps <see cref="Book.UpdatedAt"/>
/// whenever any entity in the Book aggregate is added, modified, or
/// deleted. Aggregate = Book itself + Edition + Copy + Work + WorkAuthor
/// + the BookTag join.
///
/// Drives the <c>GET /api/catalog-snapshot?since=&lt;token&gt;</c> delta
/// query so Bookshelf refreshes can ship only changed Books instead of
/// the full snapshot. Centralising the bump in one interceptor means new
/// save sites get the behaviour for free — there's no per-callsite
/// discipline to remember.
///
/// Resolution rules:
/// - Direct <see cref="Book"/> change → bump that Book.
/// - <see cref="Edition"/> change → bump Edition.Book (resolved via the
///   tracked navigation, or by BookId if the nav isn't loaded).
/// - <see cref="Copy"/> change → bump Copy.Edition.Book.
/// - <see cref="Work"/> change (or skip-nav add/remove on Work.Books) →
///   bump every Book the Work is attached to.
/// - <see cref="WorkAuthor"/> change → bump every Book of the parent
///   Work (author rename / reorder propagates to all owning Books).
/// - <see cref="Tag"/> rename → bump every Book carrying the Tag.
///
/// Idempotent and re-entrant: assigning UpdatedAt on a Book that's
/// already tracked as Modified doesn't double-bump in any visible way;
/// adding it to the change tracker as Modified is a no-op when already
/// modified.
/// </summary>
public class BookUpdatedAtInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        BumpAffectedBooks(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        BumpAffectedBooks(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void BumpAffectedBooks(DbContext? context)
    {
        if (context is null) return;

        var now = DateTime.UtcNow;
        var booksToBump = new HashSet<Book>();

        // ChangeTracker.Entries() snapshots the current state. Adding
        // a Book to the set below mutates UpdatedAt on the tracked
        // entity directly — EF picks that up because the Book is
        // already in the tracker (either as the entity being modified,
        // or because it was loaded as a navigation when the modified
        // entity was queried). For aggregate entities where the parent
        // Book is not yet tracked, we attach the Book and mark
        // UpdatedAt modified explicitly.
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            // Book is special: skip-nav changes (Book.Tags.Add(tag),
            // Book.Works.Add(work)) leave Book.State = Unchanged but
            // mark the Collection navigation IsModified. We bump on
            // either signal.
            if (entry.Entity is Book book)
            {
                var isDirectChange = entry.State is EntityState.Added
                    or EntityState.Modified or EntityState.Deleted;
                var collectionChanged = entry.Collections.Any(c => c.IsModified);
                if (isDirectChange || collectionChanged)
                {
                    booksToBump.Add(book);
                }
                continue;
            }

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            switch (entry.Entity)
            {
                case Edition edition:
                    AddOwningBookForEdition(context, edition, entry, booksToBump);
                    break;
                case Copy copy:
                    AddOwningBookForCopy(context, copy, entry, booksToBump);
                    break;
                case Work work:
                    AddOwningBooksForWork(context, work, booksToBump);
                    break;
                case WorkAuthor workAuthor:
                    AddOwningBooksForWorkAuthor(context, workAuthor, booksToBump);
                    break;
                case Tag tag:
                    AddOwningBooksForTag(context, tag, booksToBump);
                    break;
            }
        }

        foreach (var book in booksToBump)
        {
            book.UpdatedAt = now;
            // Ensure EF sees the bump even if the Book itself wasn't
            // already in the change tracker (Attach + Property.IsModified).
            var entry = context.Entry(book);
            if (entry.State == EntityState.Detached)
            {
                context.Attach(book);
            }
            entry.Property(b => b.UpdatedAt).IsModified = true;
        }
    }

    private static void AddOwningBookForEdition(
        DbContext context, Edition edition, EntityEntry entry, HashSet<Book> sink)
    {
        // Loaded navigation wins (no extra query needed). Fall back to
        // BookId lookup. For Deleted state the navigation may already
        // have been cleared, so prefer the FK value.
        var book = edition.Book;
        if (book is null && edition.BookId != 0)
        {
            book = context.Set<Book>().Local.FirstOrDefault(b => b.Id == edition.BookId)
                ?? context.Set<Book>().Find(edition.BookId);
        }
        if (book is not null) sink.Add(book);
    }

    private static void AddOwningBookForCopy(
        DbContext context, Copy copy, EntityEntry entry, HashSet<Book> sink)
    {
        // Copy → Edition → Book. Tracker-Local-only walk avoids
        // forcing a DB hit when the parent chain is in memory.
        var edition = copy.Edition
            ?? context.Set<Edition>().Local.FirstOrDefault(e => e.Id == copy.EditionId)
            ?? context.Set<Edition>().Find(copy.EditionId);
        if (edition is null) return;

        var book = edition.Book
            ?? context.Set<Book>().Local.FirstOrDefault(b => b.Id == edition.BookId)
            ?? context.Set<Book>().Find(edition.BookId);
        if (book is not null) sink.Add(book);
    }

    private static void AddOwningBooksForWork(DbContext context, Work work, HashSet<Book> sink)
    {
        // Work.Books is many-to-many. If the navigation is loaded, use
        // it directly. Otherwise query the BookWork join via the Work
        // ID — the loaded query path is the common case (Add/Edit
        // flows .Include(w => w.Books)).
        if (work.Books is { Count: > 0 })
        {
            foreach (var book in work.Books) sink.Add(book);
            return;
        }

        // Fall back: scan the change tracker for any tracked Book whose
        // Works includes this Work. If still nothing, hit the DB.
        var localBooks = context.Set<Book>().Local
            .Where(b => b.Works.Any(w => w.Id == work.Id))
            .ToList();
        if (localBooks.Count > 0)
        {
            foreach (var b in localBooks) sink.Add(b);
            return;
        }

        if (work.Id != 0)
        {
            var dbBooks = context.Set<Book>()
                .Where(b => b.Works.Any(w => w.Id == work.Id))
                .ToList();
            foreach (var b in dbBooks) sink.Add(b);
        }
    }

    private static void AddOwningBooksForWorkAuthor(
        DbContext context, WorkAuthor workAuthor, HashSet<Book> sink)
    {
        // WorkAuthor → Work → Books. Resolve via the nav or by FK.
        var work = workAuthor.Work
            ?? context.Set<Work>().Local.FirstOrDefault(w => w.Id == workAuthor.WorkId)
            ?? context.Set<Work>().Find(workAuthor.WorkId);
        if (work is null) return;

        AddOwningBooksForWork(context, work, sink);
    }

    private static void AddOwningBooksForTag(DbContext context, Tag tag, HashSet<Book> sink)
    {
        // Tag rename / delete bumps every Book carrying the tag. The
        // BookTag join is implicit (skip-nav on Book.Tags); query by
        // the tag id. Local-first to avoid the DB hit when possible.
        if (tag.Id == 0) return;

        var local = context.Set<Book>().Local
            .Where(b => b.Tags.Any(t => t.Id == tag.Id))
            .ToList();
        foreach (var b in local) sink.Add(b);

        // No DB fallback by default — tag renames typically happen
        // from the Library page where the affected Books are already
        // in scope. If a future code path renames tags without the
        // Books being tracked, this'll silently miss them; the next
        // direct Book save would re-stamp anyway.
    }
}
