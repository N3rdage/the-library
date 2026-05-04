using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

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
    public DbSet<MaintenanceLog> MaintenanceLogs => Set<MaintenanceLog>();
    public DbSet<Work> Works => Set<Work>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<WorkAuthor> WorkAuthors => Set<WorkAuthor>();
    public DbSet<IgnoredDuplicate> IgnoredDuplicates => Set<IgnoredDuplicate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

        modelBuilder.Entity<Tag>()
            .HasIndex(t => t.Name)
            .IsUnique();

        modelBuilder.Entity<Tag>()
            .HasData(new Tag { Id = 1, Name = "follow-up" });

        modelBuilder.Entity<MaintenanceLog>()
            .HasIndex(m => m.Name)
            .IsUnique();

        // Work ↔ Book is many-to-many; conventional join table BookWork.
        modelBuilder.Entity<Work>()
            .HasMany(w => w.Books)
            .WithMany(b => b.Works);

        // Work ↔ Genre is many-to-many; conventional join table GenreWork.
        modelBuilder.Entity<Work>()
            .HasMany(w => w.Genres)
            .WithMany(g => g.Works);

        // Work belongs to at most one Series. Series.Works is the inverse
        // navigation — a Series exposes its constituent Works directly.
        modelBuilder.Entity<Work>()
            .HasOne(w => w.Series)
            .WithMany(s => s.Works)
            .HasForeignKey(w => w.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);

        // Work ↔ Author is many-to-many through the WorkAuthor join entity.
        // Composite PK on (WorkId, AuthorId) — the same Author can't appear
        // twice on a single Work. Cascade on Work delete (the join row only
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
                j => j.HasKey(wa => new { wa.WorkId, wa.AuthorId }));

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

        // IgnoredDuplicate pairs: unique on (EntityType, LowerId, HigherId)
        // so the same pair can't be dismissed twice. Pair IDs are normalised
        // with the smaller ID first in DuplicateDetectionService.
        modelBuilder.Entity<IgnoredDuplicate>()
            .HasIndex(d => new { d.EntityType, d.LowerId, d.HigherId })
            .IsUnique();
    }
}
