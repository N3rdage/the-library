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

        modelBuilder.Entity<Book>()
            .HasOne(b => b.Series)
            .WithMany(s => s.Books)
            .HasForeignKey(b => b.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);

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
        // Book ↔ Genre stays in place during PR 1 (dual-write); PR 2 drops it.
        modelBuilder.Entity<Work>()
            .HasMany(w => w.Genres)
            .WithMany();

        // Work belongs to at most one Series; matches the existing Book↔Series shape.
        modelBuilder.Entity<Work>()
            .HasOne(w => w.Series)
            .WithMany()
            .HasForeignKey(w => w.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
