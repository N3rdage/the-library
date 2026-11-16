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
[Trait("Category", TestCategories.Integration)]
public class AIAssistantHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<int> SeedBookAsync(string title, string author, params Genre[] genres)
    {
        await using var db = _factory.CreateDbContext();
        foreach (var g in genres) db.Attach(g);
        var work = new Work
        {
            Title = title,
            WorkAuthors = { new WorkAuthor { Author = new Author { Name = author }, Order = 0, Role = AuthorRole.Author } },
            Genres = [.. genres],
        };
        var book = new Book { Title = title, Works = { work } };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    private async Task<Genre> SeedGenreAsync(string name, int? parentId = null)
    {
        await using var db = _factory.CreateDbContext();
        var g = new Genre { Name = name, ParentGenreId = parentId };
        db.Genres.Add(g);
        await db.SaveChangesAsync();
        return new Genre { Id = g.Id, Name = g.Name, ParentGenreId = parentId };
    }

    // --- GetBooksNeedingGenres -----------------------------------------------

    [Fact]
    public async Task GetBooksNeedingGenres_ordersByFewestGenres_thenTitle()
    {
        var fantasy = await SeedGenreAsync("AIA Fantasy");
        var comedy = await SeedGenreAsync("AIA Comedy");
        await SeedBookAsync("Zeta", "Author Z");
        await SeedBookAsync("Alpha", "Author A");
        await SeedBookAsync("Mort", "Terry Pratchett", fantasy, comedy);
        await SeedBookAsync("Beta", "Author B", fantasy);

        var rows = await new GetBooksNeedingGenresHandler(_factory).HandleAsync(new GetBooksNeedingGenres());

        Assert.Equal(new[] { "Alpha", "Zeta", "Beta", "Mort" }, rows.Select(b => b.Title));
        var mort = rows[3];
        Assert.Equal("Terry Pratchett", mort.Author);
        Assert.Equal(new[] { "AIA Comedy", "AIA Fantasy" }, mort.CurrentGenres.OrderBy(n => n));
    }

    [Fact]
    public async Task GetBooksNeedingGenres_takesFifty()
    {
        for (var i = 0; i < 52; i++) await SeedBookAsync($"Book {i:D2}", "Someone");

        var rows = await new GetBooksNeedingGenresHandler(_factory).HandleAsync(new GetBooksNeedingGenres());

        Assert.Equal(50, rows.Count);
    }

    // --- AddGenresToWork -----------------------------------------------------

    [Fact]
    public async Task AddGenresToWork_addsNamedGenres_keepsExisting_withoutParents()
    {
        var parent = await SeedGenreAsync("AIA Speculative");
        await SeedGenreAsync("AIA Space Opera", parent.Id);
        var existing = await SeedGenreAsync("AIA Adventure");
        var bookId = await SeedBookAsync("Dune", "Frank Herbert", existing);

        var result = await new AddGenresToWorkHandler(_factory).HandleAsync(
            new AddGenresToWork(bookId, ["AIA Space Opera", "AIA Adventure", "No Such Genre"]));

        Assert.Equal(new[] { "AIA Adventure", "AIA Space Opera" }, result!.OrderBy(n => n));
        await using var db = _factory.CreateDbContext();
        var names = await db.Works.Where(w => w.Books.Any(b => b.Id == bookId))
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
            await db.SaveChangesAsync();
            seriesedBookId = b.Id;
        }
        await SeedBookAsync("Foundation", "Isaac Asimov");
        await SeedBookAsync("Foundation and Empire", "Isaac Asimov");

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
            await db.SaveChangesAsync();
        }
        await SeedBookAsync("Lonely", "Someone");

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
        await SeedBookAsync("Anthology A", "Writer One");
        await SeedBookAsync("Anthology B", "Writer Two");

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
