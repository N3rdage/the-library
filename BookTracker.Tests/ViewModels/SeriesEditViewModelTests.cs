using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests.ViewModels;

// First coverage for SeriesEditViewModel — added alongside the PR3b adoption
// (the VM now dispatches the Series command handlers instead of touching EF
// directly). These guard the page contract: signatures unchanged, the in-memory
// Books list stays in sync, and the new friendly duplicate-name error surfaces.
// Series membership lives on the Book after the Work→Book cutover.
[Trait("Category", TestCategories.Integration)]
public class SeriesEditViewModelTests
{
    private readonly TestDbContextFactory _factory = new();

    private SeriesEditViewModel NewVm() => new(_factory, TestDispatcher.For(_factory));

    private async Task<int> SeedSeriesAsync(string name = "Discworld", SeriesType type = SeriesType.Series)
    {
        await using var db = _factory.CreateDbContext();
        var s = new Series { Name = name, Type = type };
        db.Series.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    // Seeds a Book carrying series membership directly (the post-cutover home).
    private async Task<int> SeedBookAsync(string title, int? seriesId = null, int? order = null, string? orderDisplay = null)
    {
        await using var db = _factory.CreateDbContext();
        var work = new Work
        {
            Title = title,
            WorkAuthors = { new WorkAuthor { Author = new Author { Name = $"Author of {title}" }, Order = 0, Role = AuthorRole.Author } },
        };
        var book = new Book
        {
            Title = title,
            Works = { work },
            SeriesId = seriesId,
            SeriesOrder = order,
            SeriesOrderDisplay = orderDisplay,
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    // --- Initialize ----------------------------------------------------------

    [Fact]
    public void InitializeNew_setsIsNew_andEmptyInput()
    {
        var vm = NewVm();
        vm.InitializeNew();

        Assert.True(vm.IsNew);
        Assert.NotNull(vm.Input);
        Assert.Null(vm.Input!.Name);
    }

    [Fact]
    public async Task InitializeAsync_missing_marksNotFound()
    {
        var vm = NewVm();
        await vm.InitializeAsync(999999);
        Assert.True(vm.NotFound);
    }

    [Fact]
    public async Task InitializeAsync_loadsDetails_andBooksInOrder()
    {
        var seriesId = await SeedSeriesAsync("Foundation");
        await SeedBookAsync("Second Foundation", seriesId, order: 2);
        await SeedBookAsync("Foundation", seriesId, order: 1);

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);

        Assert.False(vm.NotFound);
        Assert.Equal("Foundation", vm.Input!.Name);
        Assert.Equal(2, vm.Books.Count);
        Assert.Equal("Foundation", vm.Books[0].Title);       // order 1 first
        Assert.Equal("Second Foundation", vm.Books[1].Title);
    }

    // --- SaveAsync (create) --------------------------------------------------

    [Fact]
    public async Task SaveAsync_create_persistsAndReturnsId()
    {
        var vm = NewVm();
        vm.InitializeNew();
        vm.Input!.Name = "  Foundation  ";
        vm.Input.Author = "Isaac Asimov";
        vm.Input.Type = SeriesType.Series;
        vm.Input.ExpectedCount = 7;

        var id = await vm.SaveAsync(null);

        Assert.NotNull(id);
        await using var db = _factory.CreateDbContext();
        var saved = await db.Series.FindAsync(id);
        Assert.Equal("Foundation", saved!.Name);   // aggregate trimmed
        Assert.Equal(7, saved.ExpectedCount);
    }

    [Fact]
    public async Task SaveAsync_create_duplicateName_setsError_returnsNull_persistsNothing()
    {
        await SeedSeriesAsync("Discworld");

        var vm = NewVm();
        vm.InitializeNew();
        vm.Input!.Name = "discworld"; // case-insensitive clash

        var id = await vm.SaveAsync(null);

        Assert.Null(id);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        await using var db = _factory.CreateDbContext();
        Assert.Equal(1, await db.Series.CountAsync()); // nothing new created
    }

    // --- SaveAsync (edit) ----------------------------------------------------

    [Fact]
    public async Task SaveAsync_edit_updates_andSetsSuccess()
    {
        var seriesId = await SeedSeriesAsync("Old Name");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        vm.Input!.Name = "New Name";
        vm.Input.Author = "New Author";

        var id = await vm.SaveAsync(seriesId);

        Assert.Equal(seriesId, id);
        Assert.False(string.IsNullOrEmpty(vm.SuccessMessage));
        await using var db = _factory.CreateDbContext();
        Assert.Equal("New Name", (await db.Series.FindAsync(seriesId))!.Name);
    }

    [Fact]
    public async Task SaveAsync_edit_missing_marksNotFound_returnsNull()
    {
        var seriesId = await SeedSeriesAsync("Doomed");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId); // IsNew=false, Input populated
        vm.Input!.Name = "Renamed";

        // Series deleted out from under the editor between load and save.
        await using (var db = _factory.CreateDbContext())
        {
            db.Series.Remove(await db.Series.FindAsync(seriesId) ?? throw new InvalidOperationException());
            await db.SaveChangesAsync();
        }

        var id = await vm.SaveAsync(seriesId);

        Assert.Null(id);
        Assert.True(vm.NotFound);
    }

    // --- Delete --------------------------------------------------------------

    [Fact]
    public async Task DeleteSeriesAsync_removesSeries_andSetNullsBooks()
    {
        var seriesId = await SeedSeriesAsync();
        var bookId = await SeedBookAsync("Mort", seriesId, order: 1);

        var vm = NewVm();
        var ok = await vm.DeleteSeriesAsync(seriesId);

        Assert.True(ok);
        await using var db = _factory.CreateDbContext();
        Assert.Null(await db.Series.FindAsync(seriesId));
        Assert.Null((await db.Books.FindAsync(bookId))!.SeriesId); // detached, survives
    }

    // --- Book membership -----------------------------------------------------

    [Fact]
    public async Task AddBookToSeriesAsync_attaches_andAppendsRowWithOrder()
    {
        var seriesId = await SeedSeriesAsync();
        await SeedBookAsync("Colour of Magic", seriesId, order: 1);
        var newBookId = await SeedBookAsync("The Light Fantastic");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        await vm.AddBookToSeriesAsync(seriesId, newBookId);

        Assert.Equal(2, vm.Books.Count);
        var added = vm.Books.Single(b => b.Id == newBookId);
        Assert.Equal(2, added.SeriesOrder);          // appended after order 1
        await using var db = _factory.CreateDbContext();
        Assert.Equal(seriesId, (await db.Books.FindAsync(newBookId))!.SeriesId);
    }

    [Fact]
    public async Task RemoveBookFromSeriesAsync_clearsBook_andDropsRow()
    {
        var seriesId = await SeedSeriesAsync();
        var bookId = await SeedBookAsync("Edgedancer", seriesId, order: 4, orderDisplay: "4.5");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        await vm.RemoveBookFromSeriesAsync(bookId);

        Assert.Empty(vm.Books);
        await using var db = _factory.CreateDbContext();
        var book = await db.Books.FindAsync(bookId);
        Assert.Null(book!.SeriesId);
        Assert.Null(book.SeriesOrderDisplay); // dangling "4.5" cleared (the consistency fix)
    }

    [Fact]
    public async Task UpdateBookOrderAsync_parsesPersists_andUpdatesRow()
    {
        var seriesId = await SeedSeriesAsync();
        var bookId = await SeedBookAsync("Words of Radiance", seriesId, order: 2);

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        await vm.UpdateBookOrderAsync(bookId, "4.5"); // interquel

        var row = vm.Books.Single(b => b.Id == bookId);
        Assert.Equal(4, row.SeriesOrder);
        Assert.Equal("4.5", row.SeriesOrderDisplay);
        await using var db = _factory.CreateDbContext();
        var book = await db.Books.FindAsync(bookId);
        Assert.Equal(4, book!.SeriesOrder);
        Assert.Equal("4.5", book.SeriesOrderDisplay);
    }

    [Fact]
    public async Task SaveAsync_failedSaveAfterSuccess_clearsStaleSuccessMessage()
    {
        var seriesId = await SeedSeriesAsync("Keeper");
        await SeedSeriesAsync("Taken");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        vm.Input!.Name = "Keeper Renamed";
        await vm.SaveAsync(seriesId);                          // success → SuccessMessage set
        Assert.False(string.IsNullOrEmpty(vm.SuccessMessage));

        vm.Input.Name = "Taken";                              // now collides with the other series
        var id = await vm.SaveAsync(seriesId);

        Assert.Null(id);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.True(string.IsNullOrEmpty(vm.SuccessMessage)); // no dual green+red banner
    }

    [Fact]
    public async Task AddBookToSeriesAsync_calledTwiceForSameBook_addsOneRow()
    {
        var seriesId = await SeedSeriesAsync();
        var bookId = await SeedBookAsync("Sourcery");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        await vm.AddBookToSeriesAsync(seriesId, bookId);
        await vm.AddBookToSeriesAsync(seriesId, bookId);  // double-fire (e.g. double-click)

        Assert.Single(vm.Books);
        Assert.Equal(bookId, vm.Books[0].Id);
    }

    [Fact]
    public async Task SearchBooksAsync_MultiAuthorBook_ShowsSameAuthorStringAsTheAddedRow()
    {
        // Guards (a) the search query translates against real SQL and (b) the
        // dropdown author matches the row the book becomes once added — both built
        // via BookAuthorDisplay, so a multi-author book reads consistently.
        var seriesId = await SeedSeriesAsync();
        int bookId;
        await using (var db = _factory.CreateDbContext())
        {
            var preston = new Author { Name = "Douglas Preston" };
            var child = new Author { Name = "Lincoln Child" };
            var book = new Book
            {
                Title = "Relic",
                Works =
                [
                    new Work
                    {
                        Title = "Relic",
                        WorkAuthors =
                        [
                            new WorkAuthor { Author = preston, Order = 0, Role = AuthorRole.Author },
                            new WorkAuthor { Author = child, Order = 1, Role = AuthorRole.Author },
                        ],
                    },
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        vm.BookSearchTerm = "Relic";
        await vm.SearchBooksAsync(); // must translate, not throw

        var result = Assert.Single(vm.BookSearchResults);
        Assert.Equal(bookId, result.Id);
        Assert.Equal("Douglas Preston, Lincoln Child", result.Author); // lead-first, joined

        await vm.AddBookToSeriesAsync(seriesId, bookId);
        var row = Assert.Single(vm.Books);
        Assert.Equal(result.Author, row.Author); // search dropdown == added row
    }
}
