using BookTracker.Application.Books;
using BookTracker.Application.Series;
using BookTracker.Application.Wishlist;
using BookTracker.Application.Works;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the handlers behind the AI assistant page
// (GetBooksNeedingGenres, AddGenresToWork, CreateSeriesFromSuggestion,
// GetWishlistItemExists) against the SQL container. These carry the
// persistence checks the AI assistant VM tests made before the move.
//
// Seeding: each test builds its whole graph in one context. Author.Name is
// unique, so an author is created once and shared by every Work that names
// it; genres are created in the same context rather than attached as copies.
[Trait("Category", TestCategories.Integration)]
public class AIAssistantHandlerTests
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

    // --- GetBooksNeedingGenres -----------------------------------------------

    [Fact]
    public async Task GetBooksNeedingGenres_ordersByFewestGenres_thenTitle()
    {
        await using (var db = _factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "AIA Fantasy" };
            var comedy = new Genre { Name = "AIA Comedy" };
            db.Books.AddRange(
                NewBook("Zeta", new Author { Name = "Author Z" }),
                NewBook("Alpha", new Author { Name = "Author A" }),
                NewBook("Mort", new Author { Name = "Terry Pratchett" }, fantasy, comedy),
                NewBook("Beta", new Author { Name = "Author B" }, fantasy));
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
            var someone = new Author { Name = "Someone" };
            for (var i = 0; i < 52; i++) db.Books.Add(NewBook($"Book {i:D2}", someone));
            await db.SaveChangesAsync();
        }

        var rows = await new GetBooksNeedingGenresHandler(_factory).HandleAsync(new GetBooksNeedingGenres());

        Assert.Equal(50, rows.Count);
    }

    // --- AddGenresToWork -----------------------------------------------------

    [Fact]
    public async Task AddGenresToWork_addsNamedGenres_keepsExisting_withoutParents()
    {
        int bookId;
        await using (var db = _factory.CreateDbContext())
        {
            var parent = new Genre { Name = "AIA Speculative" };
            db.Genres.Add(new Genre { Name = "AIA Space Opera", ParentGenre = parent });
            var book = NewBook("Dune", new Author { Name = "Frank Herbert" }, new Genre { Name = "AIA Adventure" });
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

    // --- CreateSeriesFromSuggestion ------------------------------------------

    [Fact]
    public async Task CreateSeriesFromSuggestion_createsSeries_assignsUnseriesedMatches_inOrder()
    {
        int seriesedBookId;
        await using (var db = _factory.CreateDbContext())
        {
            var other = new Series { Name = "AIA Other", Type = SeriesType.Series };
            db.Series.Add(other);
            await db.SaveChangesAsync();
            var b = new Book { Title = "Already Seriesed" };
            b.AssignToSeries(other.Id, 4, null);
            db.Books.Add(b);

            var asimov = new Author { Name = "Isaac Asimov" };
            db.Books.AddRange(
                NewBook("Foundation", asimov),
                NewBook("Foundation and Empire", asimov));
            await db.SaveChangesAsync();
            seriesedBookId = b.Id;
        }

        var id = await new CreateSeriesFromSuggestionHandler(_factory).HandleAsync(new CreateSeriesFromSuggestion(
            "AIA Foundation", SeriesType.Series, "Asimov's saga",
            ["Foundation", "Foundation and Empire", "Already Seriesed", "Missing"]));

        await using var check = _factory.CreateDbContext();
        var series = await check.Series.Include(s => s.Books).SingleAsync(s => s.Name == "AIA Foundation");
        Assert.Equal(series.Id, id);
        Assert.Equal(SeriesType.Series, series.Type);
        Assert.Equal("Asimov's saga", series.Description);
        Assert.Equal("Isaac Asimov", series.Author);
        Assert.Equal(new[] { 1, 2 }, series.Books.Select(b => b.SeriesOrder!.Value).OrderBy(o => o));
        var untouched = await check.Books.SingleAsync(b => b.Id == seriesedBookId);
        Assert.Equal(4, untouched.SeriesOrder);
        Assert.NotEqual(series.Id, untouched.SeriesId);
    }

    [Fact]
    public async Task CreateSeriesFromSuggestion_existingName_isNoOp()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Series.Add(new Series { Name = "AIA Dup", Type = SeriesType.Collection });
            db.Books.Add(NewBook("Lonely", new Author { Name = "Someone" }));
            await db.SaveChangesAsync();
        }

        var id = await new CreateSeriesFromSuggestionHandler(_factory).HandleAsync(
            new CreateSeriesFromSuggestion("AIA Dup", SeriesType.Series, "r", ["Lonely"]));

        Assert.Null(id);
        await using var check = _factory.CreateDbContext();
        Assert.Equal(1, await check.Series.CountAsync(s => s.Name == "AIA Dup"));
        Assert.Null((await check.Books.SingleAsync(b => b.Title == "Lonely")).SeriesId);
    }

    [Fact]
    public async Task CreateSeriesFromSuggestion_mixedAuthors_leaveAuthorNull()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Books.AddRange(
                NewBook("Anthology A", new Author { Name = "Writer One" }),
                NewBook("Anthology B", new Author { Name = "Writer Two" }));
            await db.SaveChangesAsync();
        }

        await new CreateSeriesFromSuggestionHandler(_factory).HandleAsync(
            new CreateSeriesFromSuggestion("AIA Mixed", SeriesType.Collection, "r", ["Anthology A", "Anthology B"]));

        await using var check = _factory.CreateDbContext();
        var series = await check.Series.Include(s => s.Books).SingleAsync(s => s.Name == "AIA Mixed");
        Assert.Equal(SeriesType.Collection, series.Type);
        Assert.Null(series.Author);
        Assert.Equal(2, series.Books.Count);
    }

    // --- GetWishlistItemExists -----------------------------------------------

    [Fact]
    public async Task GetWishlistItemExists_matchesOnTitleAndAuthor()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.WishlistItems.Add(WishlistItem.Create("Hyperion", "Dan Simmons", WishlistPriority.Medium, [], null));
            await db.SaveChangesAsync();
        }
        var handler = new GetWishlistItemExistsHandler(_factory);

        Assert.True(await handler.HandleAsync(new GetWishlistItemExists("Hyperion", "Dan Simmons")));
        Assert.False(await handler.HandleAsync(new GetWishlistItemExists("Hyperion", "Someone Else")));
        Assert.False(await handler.HandleAsync(new GetWishlistItemExists("Endymion", "Dan Simmons")));
    }
}
