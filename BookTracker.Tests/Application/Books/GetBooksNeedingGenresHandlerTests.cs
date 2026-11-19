using BookTracker.Application.Books;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the AI assistant's genre-cleanup read model. Each test
// seeds in one context: Author.Name is unique, so an author is created once
// and shared by every Work that names it.
[Trait("Category", TestCategories.Integration)]
public class GetBooksNeedingGenresHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private static Book NewBook(string title, Author author, params Genre[] genres) => new()
    {
        Title = title,
        Works =
        [
            new Work
            {
                Title = title,
                WorkAuthors = [new WorkAuthor { Author = author, Order = 0, Role = AuthorRole.Author }],
                Genres = [.. genres],
            },
        ],
    };

    [Fact]
    public async Task GetBooksNeedingGenres_ordersByFewestGenres_thenTitle()
    {
        await using (var db = _factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "AIA Fantasy" };
            var comedy = new Genre { Name = "AIA Comedy" };
            db.Books.AddRange(
                NewBook("Zeta", Author.Create("Author Z")),
                NewBook("Alpha", Author.Create("Author A")),
                NewBook("Mort", Author.Create("Terry Pratchett"), fantasy, comedy),
                NewBook("Beta", Author.Create("Author B"), fantasy));
            await db.SaveChangesAsync();
        }

        var rows = await new GetBooksNeedingGenresHandler(_factory).HandleAsync(new GetBooksNeedingGenres());

        Assert.Equal(new[] { "Alpha", "Zeta", "Beta", "Mort" }, rows.Select(b => b.Title));
        var mort = rows[3];
        Assert.Equal("Terry Pratchett", mort.Author);
        Assert.Equal(new[] { "AIA Comedy", "AIA Fantasy" }, mort.CurrentGenres.OrderBy(n => n));
    }

    [Fact]
    public async Task GetBooksNeedingGenres_takesFifty()
    {
        await using (var db = _factory.CreateDbContext())
        {
            var someone = Author.Create("Someone");
            for (var i = 0; i < 52; i++) db.Books.Add(NewBook($"Book {i:D2}", someone));
            await db.SaveChangesAsync();
        }

        var rows = await new GetBooksNeedingGenresHandler(_factory).HandleAsync(new GetBooksNeedingGenres());

        Assert.Equal(50, rows.Count);
    }
}
