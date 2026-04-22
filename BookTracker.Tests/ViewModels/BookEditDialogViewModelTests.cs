using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

public class BookEditDialogViewModelTests
{
    [Fact]
    public async Task InitializeAsync_MissingId_MarksNotFound()
    {
        var factory = new TestDbContextFactory();
        var vm = new BookEditDialogViewModel(factory);

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
                Works = [new Work { Title = "Mort", Author = new Author { Name = "Pratchett" } }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookEditDialogViewModel(factory);
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
                Works = [new Work { Title = "w", Author = new Author { Name = "a" } }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookEditDialogViewModel(factory);
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
                Works = [new Work { Title = "w", Author = new Author { Name = "a" } }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookEditDialogViewModel(factory);
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
                Works = [new Work { Title = "w", Author = new Author { Name = "a" } }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookEditDialogViewModel(factory);
        await vm.InitializeAsync(bookId);
        vm.Title = "   ";
        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        Assert.Equal("Untouched", db2.Books.Single(b => b.Id == bookId).Title);
    }
}
