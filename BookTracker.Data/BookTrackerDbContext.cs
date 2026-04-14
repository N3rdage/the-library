using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Data;

public class BookTrackerDbContext(DbContextOptions<BookTrackerDbContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookCopy> BookCopies => Set<BookCopy>();
    public DbSet<Genre> Genres => Set<Genre>();
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

        modelBuilder.Entity<Genre>()
            .HasIndex(g => g.Name)
            .IsUnique();
    }
}
