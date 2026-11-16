using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookTracker.Tests.ViewModels;

// Pins the four data paths of the AI assistant page (genre list read, accept
// genres, create a series from a suggestion, add a recommendation to the
// wishlist) against SQL before they move behind IDispatcher.
[Trait("Category", TestCategories.Integration)]
public class AIAssistantViewModelTests
{
    private readonly TestDbContextFactory _factory = new();

    private AIAssistantViewModel CreateVm() =>
        new(_factory, new AIProviderFactory(_factory, Options.Create(new AIOptions()), NullLoggerFactory.Instance));

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

    [Fact]
    public async Task LoadBooksNeedingGenres_ordersByFewestGenres_thenTitle()
    {
        var fantasy = await SeedGenreAsync("AIA Fantasy");
        var comedy = await SeedGenreAsync("AIA Comedy");
        await SeedBookAsync("Zeta", "Author Z");
        await SeedBookAsync("Alpha", "Author A");
        await SeedBookAsync("Mort", "Terry Pratchett", fantasy, comedy);
        await SeedBookAsync("Beta", "Author B", fantasy);

        var vm = CreateVm();
        await vm.LoadBooksNeedingGenresAsync();

        Assert.True(vm.GenresLoaded);
        Assert.Equal(new[] { "Alpha", "Zeta", "Beta", "Mort" }, vm.BooksNeedingGenres.Select(b => b.Title));
        var mort = vm.BooksNeedingGenres[3];
        Assert.Equal("Terry Pratchett", mort.Author);
        Assert.Equal(new[] { "AIA Comedy", "AIA Fantasy" }, mort.CurrentGenres.OrderBy(n => n));
    }

    [Fact]
    public async Task AcceptGenreSuggestions_addsNamedGenresToPrimaryWork_withoutParents_andUpdatesRow()
    {
        var parent = await SeedGenreAsync("AIA Speculative");
        var child = await SeedGenreAsync("AIA Space Opera", parent.Id);
        var existing = await SeedGenreAsync("AIA Adventure");
        var bookId = await SeedBookAsync("Dune", "Frank Herbert", existing);

        var vm = CreateVm();
        await vm.LoadBooksNeedingGenresAsync();
        await vm.AcceptGenreSuggestionsAsync(bookId, ["AIA Space Opera", "AIA Adventure", "No Such Genre"]);

        await using var db = _factory.CreateDbContext();
        var names = await db.Works.Where(w => w.Books.Any(b => b.Id == bookId))
            .SelectMany(w => w.Genres).Select(g => g.Name).ToListAsync();
        Assert.Equal(new[] { "AIA Adventure", "AIA Space Opera" }, names.OrderBy(n => n));   // parent not auto-added
        Assert.Equal(new[] { "AIA Adventure", "AIA Space Opera" },
            vm.BooksNeedingGenres.Single(b => b.Id == bookId).CurrentGenres.OrderBy(n => n));
        Assert.Null(vm.CurrentSuggestion);
    }

    [Fact]
    public async Task CreateCollectionFromSuggestion_createsSeries_assignsUnseriesedMatches_inOrder()
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

        var vm = CreateVm();
        await vm.CreateCollectionFromSuggestionAsync(new CollectionGrouping(
            "AIA Foundation", "series", "Asimov's saga", ["Foundation", "Foundation and Empire", "Already Seriesed", "Missing"]));

        await using var check = _factory.CreateDbContext();
        var series = await check.Series.Include(s => s.Books).SingleAsync(s => s.Name == "AIA Foundation");
        Assert.Equal(SeriesType.Series, series.Type);
        Assert.Equal("Asimov's saga", series.Description);
        Assert.Equal("Isaac Asimov", series.Author);
        Assert.Equal(2, series.Books.Count);
        Assert.All(series.Books, b => Assert.InRange(b.SeriesOrder!.Value, 1, 2));
        Assert.Equal(new[] { 1, 2 }, series.Books.Select(b => b.SeriesOrder!.Value).OrderBy(o => o));
        var untouched = await check.Books.SingleAsync(b => b.Id == seriesedBookId);
        Assert.Equal(4, untouched.SeriesOrder);
        Assert.NotEqual(series.Id, untouched.SeriesId);
    }

    [Fact]
    public async Task CreateCollectionFromSuggestion_existingName_isNoOp()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Series.Add(new Series { Name = "AIA Dup", Type = SeriesType.Collection });
            await db.SaveChangesAsync();
        }
        await SeedBookAsync("Lonely", "Someone");

        await CreateVm().CreateCollectionFromSuggestionAsync(
            new CollectionGrouping("AIA Dup", "Series", "r", ["Lonely"]));

        await using var check = _factory.CreateDbContext();
        Assert.Equal(1, await check.Series.CountAsync(s => s.Name == "AIA Dup"));
        Assert.Null((await check.Books.SingleAsync(b => b.Title == "Lonely")).SeriesId);
    }

    [Fact]
    public async Task CreateCollectionFromSuggestion_nonSeriesType_isCollection_andMixedAuthorsLeaveAuthorNull()
    {
        await SeedBookAsync("Anthology A", "Writer One");
        await SeedBookAsync("Anthology B", "Writer Two");

        await CreateVm().CreateCollectionFromSuggestionAsync(
            new CollectionGrouping("AIA Mixed", "Themed", "r", ["Anthology A", "Anthology B"]));

        await using var check = _factory.CreateDbContext();
        var series = await check.Series.SingleAsync(s => s.Name == "AIA Mixed");
        Assert.Equal(SeriesType.Collection, series.Type);
        Assert.Null(series.Author);
    }

    [Fact]
    public async Task AddRecommendationToWishlist_addsMediumItem_andSkipsDuplicateTitleAuthor()
    {
        var vm = CreateVm();
        var rec = new BookRecommendation("Hyperion", "Dan Simmons", "because", null);

        await vm.AddRecommendationToWishlistAsync(rec);
        await vm.AddRecommendationToWishlistAsync(rec);
        await vm.AddRecommendationToWishlistAsync(rec with { Author = "Someone Else" });

        await using var db = _factory.CreateDbContext();
        var items = await db.WishlistItems.Where(w => w.Title == "Hyperion").ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(WishlistPriority.Medium, i.Priority));
    }
}
