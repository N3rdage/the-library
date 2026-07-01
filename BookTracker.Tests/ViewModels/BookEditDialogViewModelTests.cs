using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Integration)]
public class BookEditDialogViewModelTests
{
    [Fact]
    public async Task InitializeAsync_MissingId_MarksNotFound()
    {
        var factory = new TestDbContextFactory();
        var vm = new BookEditDialogViewModel(factory, TestDispatcher.For(factory));

        await vm.InitializeAsync(999);

        Assert.True(vm.NotFound);
    }

    [Fact]
    public async Task InitializeAsync_LoadsCurrentValues()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var book = new Book
            {
                Title = "Mort",
                Category = BookCategory.Fiction,
                DefaultCoverArtUrl = "https://example.com/mort.jpg",
                Works = [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "Pratchett" }, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookEditDialogViewModel(factory, TestDispatcher.For(factory));
        await vm.InitializeAsync(bookId);

        Assert.False(vm.NotFound);
        Assert.Equal("Mort", vm.Title);
        Assert.Equal(BookCategory.Fiction, vm.Category);
        Assert.Equal("https://example.com/mort.jpg", vm.CoverUrl);
    }

    [Fact]
    public async Task SaveAsync_PersistsAllFields()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var book = new Book
            {
                Title = "Old title",
                Category = BookCategory.Fiction,
                Works = [new Work { Title = "w", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "a" }, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookEditDialogViewModel(factory, TestDispatcher.For(factory));
        await vm.InitializeAsync(bookId);
        vm.Title = "  New title  ";
        vm.Category = BookCategory.NonFiction;
        vm.CoverUrl = "  https://example.com/new.jpg  ";
        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        var saved = db2.Books.Single(b => b.Id == bookId);
        Assert.Equal("New title", saved.Title);
        Assert.Equal(BookCategory.NonFiction, saved.Category);
        Assert.Equal("https://example.com/new.jpg", saved.DefaultCoverArtUrl);
    }

    [Fact]
    public async Task SaveAsync_BlankCoverUrl_PersistsAsNull()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var book = new Book
            {
                Title = "T",
                DefaultCoverArtUrl = "https://old.example.com",
                Works = [new Work { Title = "w", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "a" }, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookEditDialogViewModel(factory, TestDispatcher.For(factory));
        await vm.InitializeAsync(bookId);
        vm.CoverUrl = "   ";
        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        Assert.Null(db2.Books.Single(b => b.Id == bookId).DefaultCoverArtUrl);
    }

    [Fact]
    public async Task SaveAsync_BlankTitle_IsNoOp()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var book = new Book
            {
                Title = "Untouched",
                Works = [new Work { Title = "w", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "a" }, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookEditDialogViewModel(factory, TestDispatcher.For(factory));
        await vm.InitializeAsync(bookId);
        vm.Title = "   ";
        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        Assert.Equal("Untouched", db2.Books.Single(b => b.Id == bookId).Title);
    }

    [Fact]
    public async Task SwitchingSeries_DoesNotCarryStaleOrderIntoNewSeries()
    {
        // Regression (arc-end review): a book in series A at #5, edited and switched
        // to series B without touching the order field, must NOT save as B #5. The
        // dropdown routes through OnSeriesChanged, which resets the order.
        var factory = new TestDbContextFactory();
        int bookId, seriesBId;
        using (var db = factory.CreateDbContext())
        {
            var seriesA = new Series { Name = "Witcher", Type = SeriesType.Series };
            var seriesB = new Series { Name = "Dune", Type = SeriesType.Series };
            db.Series.AddRange(seriesA, seriesB);
            var book = new Book
            {
                Title = "T",
                Series = seriesA, SeriesOrder = 5,
                Works = [new Work { Title = "w", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "a" }, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            seriesBId = seriesB.Id;
        }

        var vm = new BookEditDialogViewModel(factory, TestDispatcher.For(factory));
        await vm.InitializeAsync(bookId);
        Assert.Equal("5", vm.SeriesOrderInput);      // loaded order for series A

        vm.OnSeriesChanged(seriesBId);               // user switches the dropdown to Dune
        Assert.Null(vm.SeriesOrderInput);            // stale "5" cleared

        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        var saved = db2.Books.Single(b => b.Id == bookId);
        Assert.Equal(seriesBId, saved.SeriesId);
        Assert.Null(saved.SeriesOrder);              // NOT 5 — the old series' order didn't ride over
    }

    [Fact]
    public async Task OnSeriesChanged_SameSeries_KeepsLoadedOrder()
    {
        // Re-selecting the current series (a no-op change) must not wipe the order.
        var factory = new TestDbContextFactory();
        int bookId, seriesId;
        using (var db = factory.CreateDbContext())
        {
            var series = new Series { Name = "Witcher", Type = SeriesType.Series };
            db.Series.Add(series);
            var book = new Book
            {
                Title = "T",
                Series = series, SeriesOrder = 5,
                Works = [new Work { Title = "w", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "a" }, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            seriesId = series.Id;
        }

        var vm = new BookEditDialogViewModel(factory, TestDispatcher.For(factory));
        await vm.InitializeAsync(bookId);
        vm.OnSeriesChanged(seriesId);                // same series → no reset
        Assert.Equal("5", vm.SeriesOrderInput);
    }
}
