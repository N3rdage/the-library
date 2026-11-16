using BookTracker.Application.Works;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the AI assistant's "accept genre suggestions" command.
[Trait("Category", TestCategories.Integration)]
public class AddGenresToWorkHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task AddGenresToWork_addsNamedGenres_keepsExisting_withoutParents()
    {
        int bookId;
        await using (var db = _factory.CreateDbContext())
        {
            var parent = new Genre { Name = "AIA Speculative" };
            db.Genres.Add(new Genre { Name = "AIA Space Opera", ParentGenre = parent });
            var book = new Book
            {
                Title = "Dune",
                Works =
                [
                    new Work
                    {
                        Title = "Dune",
                        WorkAuthors = [new WorkAuthor { Author = new Author { Name = "Frank Herbert" }, Order = 0, Role = AuthorRole.Author }],
                        Genres = [new Genre { Name = "AIA Adventure" }],
                    },
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var result = await new AddGenresToWorkHandler(_factory).HandleAsync(
            new AddGenresToWork(bookId, ["AIA Space Opera", "AIA Adventure", "No Such Genre"]));

        Assert.Equal(new[] { "AIA Adventure", "AIA Space Opera" }, result!.OrderBy(n => n));
        await using var check = _factory.CreateDbContext();
        var names = await check.Works.Where(w => w.Books.Any(b => b.Id == bookId))
            .SelectMany(w => w.Genres).Select(g => g.Name).ToListAsync();
        Assert.Equal(new[] { "AIA Adventure", "AIA Space Opera" }, names.OrderBy(n => n)); // parent not auto-added
    }

    [Fact]
    public async Task AddGenresToWork_missingBook_returnsNull()
    {
        var result = await new AddGenresToWorkHandler(_factory).HandleAsync(new AddGenresToWork(999999, ["X"]));
        Assert.Null(result);
    }
}
