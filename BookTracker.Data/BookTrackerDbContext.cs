using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BookTracker.Data;

public class BookTrackerDbContext(DbContextOptions<BookTrackerDbContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Edition> Editions => Set<Edition>();
    public DbSet<Copy> Copies => Set<Copy>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Publisher> Publishers => Set<Publisher>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<WishlistItemIsbn> WishlistItemIsbns => Set<WishlistItemIsbn>();
    public DbSet<MaintenanceLog> MaintenanceLogs => Set<MaintenanceLog>();
    public DbSet<Work> Works => Set<Work>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<WorkAuthor> WorkAuthors => Set<WorkAuthor>();
    public DbSet<IgnoredDuplicate> IgnoredDuplicates => Set<IgnoredDuplicate>();
    public DbSet<CoverMirrorState> CoverMirrorStates => Set<CoverMirrorState>();
    public DbSet<AiUsage> AiUsages => Set<AiUsage>();
    public DbSet<Loan> Loans => Set<Loan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Book.UpdatedAt: default to SQL Server's GETUTCDATE() so the
        // migration backfills existing rows with a real timestamp.
        // Indexed because the snapshot delta query
        // (CatalogSnapshotService with ?since=<token>) filters on it.
        //
        // Value converter stamps Kind=Utc on every read. SQL Server's
        // datetime2 doesn't store Kind, so EF returns Kind=Unspecified
        // by default. System.Text.Json then serialises Unspecified
        // timestamps WITHOUT the trailing "Z", non-UTC clients parse
        // them as Local, and their .ToUniversalTime() shifts the
        // watermark by the client's timezone offset on the next
        // ?since= round-trip. The delta filter `UpdatedAt > since`
        // ends up matching every Book edited in the last N hours
        // instead of just the ones changed since the last sync —
        // silently defeating delta-sync's bandwidth savings.
        // First diagnosed 2026-05-14 on an NZ phone: 1 book added,
        // delta returned 1,146 books (whole catalogue).
        modelBuilder.Entity<Book>()
            .Property(b => b.UpdatedAt)
            .HasDefaultValueSql("GETUTCDATE()")
            .HasConversion(new ValueConverter<DateTime, DateTime>(
                v => v,
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        modelBuilder.Entity<Book>()
            .HasIndex(b => b.UpdatedAt);

        // Soft-delete: a global query filter hides tombstoned Books from
        // every normal query (Library, View, search, merge, …). The dead
        // husks survive only to power tombstone emission in the catalog
        // snapshot's deletedIds[] for Bookshelf clients. The tombstone
        // emission query opts out with IgnoreQueryFilters().
        //
        // Indexed because the snapshot delta path queries `DeletedAt >
        // since` for tombstones, and the index narrows the husk-only
        // subset cheaply (most rows have DeletedAt = NULL, which a
        // filtered index skips entirely).
        //
        // Same Kind=Utc-on-read converter as UpdatedAt above. DeletedAt
        // doesn't cross the wire as a DateTime today (only the Id ships
        // in deletedIds[]), but the LatestUpdatedAt watermark factors
        // max(UpdatedAt, DeletedAt) on the server, so a Kind-Unspecified
        // DeletedAt would re-introduce the timezone-shift bug on the
        // pure-tombstone-delta path.
        modelBuilder.Entity<Book>()
            .HasQueryFilter(b => b.DeletedAt == null);
        modelBuilder.Entity<Book>()
            .HasIndex(b => b.DeletedAt)
            .HasFilter("[DeletedAt] IS NOT NULL");
        modelBuilder.Entity<Book>()
            .Property(b => b.DeletedAt)
            .HasConversion(new ValueConverter<DateTime?, DateTime?>(
                v => v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null));

        modelBuilder.Entity<Edition>()
            .HasOne(e => e.Book)
            .WithMany(b => b.Editions)
            .HasForeignKey(e => e.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        // Filtered unique index — no-ISBN editions (pre-1974 books) skip
        // the constraint, so multiple null-ISBN editions can coexist.
        modelBuilder.Entity<Edition>()
            .HasIndex(e => e.Isbn)
            .IsUnique()
            .HasFilter("[Isbn] IS NOT NULL");

        modelBuilder.Entity<Edition>()
            .HasOne(e => e.Publisher)
            .WithMany(p => p.Editions)
            .HasForeignKey(e => e.PublisherId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Copy>()
            .HasOne(c => c.Edition)
            .WithMany(e => e.Copies)
            .HasForeignKey(c => c.EditionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Edition>()
            .Navigation(e => e.Copies)
            .HasField("_copies")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Loan: part of the Book aggregate through Copy, so it goes when the
        // Copy goes. The filtered unique index allows any number of returned
        // loans per copy but at most one open one (ReturnedOn IS NULL).
        modelBuilder.Entity<Loan>()
            .HasOne(l => l.Copy)
            .WithMany(c => c.Loans)
            .HasForeignKey(l => l.CopyId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Loan>()
            .HasIndex(l => l.CopyId)
            .IsUnique()
            .HasFilter("[ReturnedOn] IS NULL")
            .HasDatabaseName("IX_Loans_CopyId_Open");
        modelBuilder.Entity<Copy>()
            .Ignore(c => c.OpenLoan);
        modelBuilder.Entity<Copy>()
            .Ignore(c => c.IsOwned);
        modelBuilder.Entity<Loan>()
            .Ignore(l => l.IsOpen);

        modelBuilder.Entity<Genre>()
            .HasIndex(g => g.Name)
            .IsUnique();

        modelBuilder.Entity<Genre>()
            .HasOne(g => g.ParentGenre)
            .WithMany(g => g.Children)
            .HasForeignKey(g => g.ParentGenreId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Publisher>()
            .HasIndex(p => p.Name)
            .IsUnique();

        modelBuilder.Entity<Series>()
            .HasIndex(s => s.Name)
            .IsUnique();

        modelBuilder.Entity<WishlistItem>()
            .HasOne(w => w.Series)
            .WithMany()
            .HasForeignKey(w => w.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<WishlistItem>()
            .HasIndex(w => w.Isbn);

        modelBuilder.Entity<WishlistItemIsbn>()
            .HasOne(i => i.WishlistItem)
            .WithMany(w => w.Isbns)
            .HasForeignKey(i => i.WishlistItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WishlistItem>()
            .Navigation(w => w.Isbns)
            .HasField("_isbns")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<WishlistItemIsbn>()
            .HasIndex(i => i.Isbn);

        modelBuilder.Entity<Tag>()
            .HasIndex(t => t.Name)
            .IsUnique();

        modelBuilder.Entity<Tag>()
            .HasData(
                new Tag { Id = 1, Name = "follow-up" },
                new Tag { Id = 2, Name = "format:graphic-novel" },
                new Tag { Id = 3, Name = "format:short-stories" });

        modelBuilder.Entity<MaintenanceLog>()
            .HasIndex(m => m.Name)
            .IsUnique();

        // Work ↔ Book is many-to-many through the explicit BookWork join entity,
        // which carries BookWork.Order — each Work's display position WITHIN a
        // given Book (a Work in several books orders independently per book).
        // Both skip-navigations (Work.Books, Book.Works) and the explicit join
        // collections (Work.BookWorks, Book.BookWorks) are kept — skip-nav for
        // "which books contain this work" semantics, explicit join for ordered
        // display + reorder via BookWork.Order. Cascade both directions: the
        // join row exists only while both endpoints do.
        //
        // The FK columns keep their original skip-nav convention names
        // (BooksId / WorksId) so promoting the implicit join to this explicit
        // entity is an add-the-Order-column migration with no data-moving rename.
        modelBuilder.Entity<Work>()
            .HasMany(w => w.Books)
            .WithMany(b => b.Works)
            .UsingEntity<BookWork>(
                j => j.HasOne(bw => bw.Book)
                      .WithMany(b => b.BookWorks)
                      .HasForeignKey(bw => bw.BookId)
                      .OnDelete(DeleteBehavior.Cascade),
                j => j.HasOne(bw => bw.Work)
                      .WithMany(w => w.BookWorks)
                      .HasForeignKey(bw => bw.WorkId)
                      .OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.HasKey(bw => new { bw.BookId, bw.WorkId });
                    j.Property(bw => bw.BookId).HasColumnName("BooksId");
                    j.Property(bw => bw.WorkId).HasColumnName("WorksId");
                });

        // Work ↔ Genre is many-to-many; conventional join table GenreWork.
        modelBuilder.Entity<Work>()
            .HasMany(w => w.Genres)
            .WithMany(g => g.Works);

        // Book belongs to at most one Series. Series.Books is the inverse
        // navigation — the Book is installment N of a publication series.
        modelBuilder.Entity<Book>()
            .HasOne(b => b.Series)
            .WithMany(s => s.Books)
            .HasForeignKey(b => b.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Series>()
            .Navigation(s => s.Books)
            .HasField("_books")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Work ↔ Author is many-to-many through the WorkAuthor join entity.
        // Composite PK on (WorkId, AuthorId, Role) — the same Author can't
        // appear twice on a single Work in the same role, but CAN hold
        // multiple roles (Tolkien is Author + Illustrator on *The Hobbit*).
        // Cascade on Work delete (the join row only
        // exists as long as the Work does); Restrict on Author delete (don't
        // allow deleting an Author that's still credited on a Work).
        //
        // Both skip-navigations (Work.Authors, Author.Works) and the explicit
        // join collections (Work.WorkAuthors, Author.WorkAuthors) are kept —
        // skip-nav for "any author of this work" semantics, explicit join for
        // ordered display via WorkAuthor.Order.
        modelBuilder.Entity<Work>()
            .HasMany(w => w.Authors)
            .WithMany(a => a.Works)
            .UsingEntity<WorkAuthor>(
                j => j.HasOne(wa => wa.Author)
                      .WithMany(a => a.WorkAuthors)
                      .HasForeignKey(wa => wa.AuthorId)
                      .OnDelete(DeleteBehavior.Restrict),
                j => j.HasOne(wa => wa.Work)
                      .WithMany(w => w.WorkAuthors)
                      .HasForeignKey(wa => wa.WorkId)
                      .OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.HasKey(wa => new { wa.WorkId, wa.AuthorId, wa.Role });
                    j.Property(wa => wa.Role).HasDefaultValue(AuthorRole.Author);
                });

        var work = modelBuilder.Entity<Work>();
        work.Navigation(w => w.WorkAuthors).HasField("_workAuthors").UsePropertyAccessMode(PropertyAccessMode.Field);
        work.Navigation(w => w.Authors).HasField("_authors").UsePropertyAccessMode(PropertyAccessMode.Field);
        work.Navigation(w => w.Genres).HasField("_genres").UsePropertyAccessMode(PropertyAccessMode.Field);
        work.Navigation(w => w.Books).HasField("_books").UsePropertyAccessMode(PropertyAccessMode.Field);
        work.Navigation(w => w.BookWorks).HasField("_bookWorks").UsePropertyAccessMode(PropertyAccessMode.Field);

        var book = modelBuilder.Entity<Book>();
        book.Navigation(b => b.Editions).HasField("_editions").UsePropertyAccessMode(PropertyAccessMode.Field);
        book.Navigation(b => b.Tags).HasField("_tags").UsePropertyAccessMode(PropertyAccessMode.Field);
        book.Navigation(b => b.Works).HasField("_works").UsePropertyAccessMode(PropertyAccessMode.Field);
        book.Navigation(b => b.BookWorks).HasField("_bookWorks").UsePropertyAccessMode(PropertyAccessMode.Field);

        // The inverse collections on the other entities: EF fills them, and
        // every write goes through the owning side's methods.
        modelBuilder.Entity<Author>().Navigation(a => a.Works).HasField("_works").UsePropertyAccessMode(PropertyAccessMode.Field);
        modelBuilder.Entity<Author>().Navigation(a => a.WorkAuthors).HasField("_workAuthors").UsePropertyAccessMode(PropertyAccessMode.Field);
        modelBuilder.Entity<Genre>().Navigation(g => g.Children).HasField("_children").UsePropertyAccessMode(PropertyAccessMode.Field);
        modelBuilder.Entity<Genre>().Navigation(g => g.Works).HasField("_works").UsePropertyAccessMode(PropertyAccessMode.Field);
        modelBuilder.Entity<Publisher>().Navigation(p => p.Editions).HasField("_editions").UsePropertyAccessMode(PropertyAccessMode.Field);
        modelBuilder.Entity<Tag>().Navigation(t => t.Books).HasField("_books").UsePropertyAccessMode(PropertyAccessMode.Field);
        modelBuilder.Entity<Copy>().Navigation(c => c.Loans).HasField("_loans").UsePropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<Author>()
            .HasIndex(a => a.Name)
            .IsUnique();

        // Self-referential alias relationship — pen names point at their
        // canonical Author. NoAction on delete because a CASCADE could
        // wipe the canonical's whole alias graph if the canonical is
        // deleted; manual cleanup via the /authors UI is safer.
        modelBuilder.Entity<Author>()
            .HasOne(a => a.CanonicalAuthor)
            .WithMany(a => a.Aliases)
            .HasForeignKey(a => a.CanonicalAuthorId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Author>()
            .Navigation(a => a.Aliases)
            .HasField("_aliases")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // IgnoredDuplicate pairs: unique on (EntityType, LowerId, HigherId)
        // so the same pair can't be dismissed twice. Pair IDs are normalised
        // with the smaller ID first by the DismissDuplicatePair handler.
        modelBuilder.Entity<IgnoredDuplicate>()
            .HasIndex(d => new { d.EntityType, d.LowerId, d.HigherId })
            .IsUnique();

        // CoverMirrorState: at most one row per cover column, so unique on
        // (Target, TargetId). The attempt timestamps use the same
        // Kind=Utc-on-read converter as Book.UpdatedAt so the backoff
        // comparison against DateTime.UtcNow never mixes kinds.
        modelBuilder.Entity<CoverMirrorState>()
            .HasIndex(s => new { s.Target, s.TargetId })
            .IsUnique();
        modelBuilder.Entity<CoverMirrorState>()
            .Property(s => s.LastAttemptUtc)
            .HasConversion(UtcOnRead);
        modelBuilder.Entity<CoverMirrorState>()
            .Property(s => s.NextAttemptUtc)
            .HasConversion(UtcOnRead);

        // AiUsage: the summary query filters on OccurredAt (this month vs all
        // time), so it is indexed; same Kind=Utc-on-read converter so the
        // month boundary is compared in one kind.
        modelBuilder.Entity<AiUsage>()
            .HasIndex(u => u.OccurredAt);
        modelBuilder.Entity<AiUsage>()
            .Property(u => u.OccurredAt)
            .HasConversion(UtcOnRead);
    }

    private static readonly ValueConverter<DateTime, DateTime> UtcOnRead = new(
        v => v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
