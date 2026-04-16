using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Data;

public class BookTrackerDbContext(DbContextOptions<BookTrackerDbContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookCopy> BookCopies => Set<BookCopy>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Publisher> Publishers => Set<Publisher>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BookCopy>()
            .HasOne(c => c.Book)
            .WithMany(b => b.Copies)
            .HasForeignKey(c => c.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BookCopy>()
            .HasIndex(c => c.Isbn);

        modelBuilder.Entity<BookCopy>()
            .HasOne(c => c.Publisher)
            .WithMany(p => p.Copies)
            .HasForeignKey(c => c.PublisherId)
            .OnDelete(DeleteBehavior.Restrict);

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

        modelBuilder.Entity<Tag>()
            .HasIndex(t => t.Name)
            .IsUnique();

        modelBuilder.Entity<Tag>()
            .HasData(new Tag { Id = 1, Name = "follow-up" });
    }
}
