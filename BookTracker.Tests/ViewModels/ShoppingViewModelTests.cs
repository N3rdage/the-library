using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Integration)]
public class ShoppingViewModelTests
{
    [Fact]
    public async Task SearchByIsbnAsync_ExistingBook_PopulatesAuthorWithoutCrashing()
    {
        // Regression for the NullReferenceException that surfaced on the
        // Shopping page when an ISBN matched an existing edition. The query
        // was missing .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author), so PrimaryAuthor dereferenced
        // a null navigation property. This test seeds a book + edition and
        // ensures the result comes back with the author populated.
        var factory = new TestDbContextFactory();
        const string isbn = "9780552131063";

        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Terry Pratchett" };
            var book = new Book
            {
                Title = "Mort",
                Works = [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }],
                Editions =
                [
                    new Edition
                    {
                        Isbn = isbn,
                        Format = BookFormat.MassMarketPaperback,
                        Copies = [new Copy { Condition = BookCondition.Good }],
                    }
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
        }

        var vm = new ShoppingViewModel(factory);
        await vm.SearchByIsbnAsync(isbn);

        Assert.NotNull(vm.Result);
        Assert.True(vm.Result!.Found);
        Assert.Equal("Terry Pratchett", vm.Result.Author);
        Assert.Equal("Mort", vm.Result.Title);
        Assert.Equal(1, vm.Result.CopyCount);
    }

    [Fact]
    public async Task SearchByIsbnAsync_NoMatch_ReturnsNotFound()
    {
        var factory = new TestDbContextFactory();
        var vm = new ShoppingViewModel(factory);
        await vm.SearchByIsbnAsync("0000000000000");

        Assert.NotNull(vm.Result);
        Assert.False(vm.Result!.Found);
    }

    [Fact]
    public async Task SelectBookAsync_PopulatesAuthorWithoutCrashing()
    {
        // Same regression surface as SearchByIsbnAsync — SelectBookAsync
        // also loaded Works without Authors.
        var factory = new TestDbContextFactory();
        int bookId;

        using (var db = factory.CreateDbContext())
        {
            var book = new Book
            {
                Title = "Good Omens",
                Works = [new Work { Title = "Good Omens", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "Terry Pratchett & Neil Gaiman" }, Order = 0 }] }],
                Editions = [new Edition { Isbn = "x", Copies = [new Copy { Condition = BookCondition.Good }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new ShoppingViewModel(factory);
        await vm.SelectBookAsync(bookId);

        Assert.NotNull(vm.Result);
        Assert.Equal("Terry Pratchett & Neil Gaiman", vm.Result!.Author);
    }
}
