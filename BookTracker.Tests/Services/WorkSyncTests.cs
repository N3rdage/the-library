using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

public class WorkSyncTests
{
    [Fact]
    public void EnsureWork_CreatesWorkMirroringBook_WhenNoneExists()
    {
        var genre = new Genre { Id = 1, Name = "Mystery" };
        var series = new Series { Id = 7, Name = "Poirot" };
        var book = new Book
        {
            Title = "Murder on the Orient Express",
            Subtitle = "A Hercule Poirot Mystery",
            Author = "Agatha Christie",
            Genres = [genre],
            SeriesId = series.Id,
            Series = series,
            SeriesOrder = 9,
        };

        WorkSync.EnsureWork(book);

        var work = Assert.Single(book.Works);
        Assert.Equal(book.Title, work.Title);
        Assert.Equal(book.Subtitle, work.Subtitle);
        Assert.Equal(book.Author, work.Author);
        Assert.Equal(series.Id, work.SeriesId);
        Assert.Equal(book.SeriesOrder, work.SeriesOrder);
        Assert.Single(work.Genres, g => g.Id == genre.Id);
    }

    [Fact]
    public void EnsureWork_UpdatesExistingWork_WhenBookFieldsChanged()
    {
        var oldGenre = new Genre { Id = 1, Name = "Romance" };
        var newGenre = new Genre { Id = 2, Name = "Mystery" };

        var existingWork = new Work
        {
            Title = "Old title",
            Subtitle = null,
            Author = "Wrong author",
            Genres = [oldGenre],
        };
        var book = new Book
        {
            Title = "Corrected title",
            Subtitle = "New subtitle",
            Author = "Correct author",
            Genres = [newGenre],
            SeriesId = 99,
            SeriesOrder = 3,
            Works = [existingWork],
        };

        WorkSync.EnsureWork(book);

        var work = Assert.Single(book.Works);
        Assert.Same(existingWork, work); // didn't replace, mutated
        Assert.Equal("Corrected title", work.Title);
        Assert.Equal("New subtitle", work.Subtitle);
        Assert.Equal("Correct author", work.Author);
        Assert.Equal(99, work.SeriesId);
        Assert.Equal(3, work.SeriesOrder);
        Assert.Single(work.Genres, g => g.Id == newGenre.Id);
        Assert.DoesNotContain(work.Genres, g => g.Id == oldGenre.Id);
    }

    [Fact]
    public void EnsureWork_IsIdempotent_WhenAlreadyInSync()
    {
        var genre = new Genre { Id = 1, Name = "Mystery" };
        var book = new Book
        {
            Title = "T",
            Author = "A",
            Genres = [genre],
        };

        WorkSync.EnsureWork(book);
        WorkSync.EnsureWork(book);

        var work = Assert.Single(book.Works);
        Assert.Single(work.Genres, g => g.Id == genre.Id);
    }
}
