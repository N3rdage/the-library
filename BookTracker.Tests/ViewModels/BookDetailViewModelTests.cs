using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

public class BookDetailViewModelTests
{
    [Fact]
    public async Task InitializeAsync_MissingId_MarksNotFound()
    {
        var factory = new TestDbContextFactory();
        var vm = new BookDetailViewModel(factory);

        await vm.InitializeAsync(999);

        Assert.True(vm.NotFound);
        Assert.Null(vm.Book);
    }

    [Fact]
    public async Task InitializeAsync_SingleWorkBook_ShapesBasicDetails()
    {
        var factory = new TestDbContextFactory();

        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Ursula K. Le Guin" };
            var genre = new Genre { Name = "Fantasy" };
            var book = new Book
            {
                Title = "A Wizard of Earthsea",
                Status = BookStatus.Read,
                Rating = 5,
                Notes = "Gorgeous prose.",
                Works =
                [
                    new Work
                    {
                        Title = "A Wizard of Earthsea",
                        Author = author,
                        Genres = [genre],
                    }
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookDetailViewModel(factory);
        await vm.InitializeAsync(bookId);

        Assert.False(vm.NotFound);
        Assert.NotNull(vm.Book);
        Assert.True(vm.IsSingleWork);
        Assert.Equal("A Wizard of Earthsea", vm.Book!.Title);
        Assert.Equal(BookStatus.Read, vm.Book.Status);
        Assert.Equal(5, vm.Book.Rating);
        Assert.Single(vm.Book.Works);
        Assert.Equal("Ursula K. Le Guin", vm.Book.Works[0].AuthorName);
        Assert.Contains("Fantasy", vm.Book.Works[0].Genres);
    }

    [Fact]
    public async Task InitializeAsync_MultiWorkBook_FlagsCompendiumAndOrdersBySeries()
    {
        var factory = new TestDbContextFactory();

        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "William Shakespeare" };
            var series = new Series { Name = "Shakespeare's Plays", Type = SeriesType.Collection };
            var book = new Book
            {
                Title = "Complete Works",
                Works =
                [
                    // Intentionally out of order — VM should sort by SeriesOrder.
                    new Work { Title = "Macbeth", Author = author, Series = series, SeriesOrder = 3 },
                    new Work { Title = "Hamlet", Author = author, Series = series, SeriesOrder = 1 },
                    new Work { Title = "Othello", Author = author, Series = series, SeriesOrder = 2 },
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookDetailViewModel(factory);
        await vm.InitializeAsync(bookId);

        Assert.False(vm.IsSingleWork);
        Assert.Equal(3, vm.Book!.Works.Count);
        Assert.Equal("Hamlet", vm.Book.Works[0].Title);
        Assert.Equal("Othello", vm.Book.Works[1].Title);
        Assert.Equal("Macbeth", vm.Book.Works[2].Title);
    }

    [Fact]
    public async Task InitializeAsync_EditionsAndCopies_CountsAndNests()
    {
        var factory = new TestDbContextFactory();

        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Terry Pratchett" };
            var book = new Book
            {
                Title = "Mort",
                Works = [new Work { Title = "Mort", Author = author }],
                Editions =
                [
                    new Edition
                    {
                        Isbn = "9780552131063",
                        Format = BookFormat.MassMarketPaperback,
                        Copies =
                        [
                            new Copy { Condition = BookCondition.Good },
                            new Copy { Condition = BookCondition.Fair },
                        ],
                    },
                    new Edition
                    {
                        Isbn = "9780061020681",
                        Format = BookFormat.Hardcover,
                        Copies = [new Copy { Condition = BookCondition.Fine }],
                    },
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookDetailViewModel(factory);
        await vm.InitializeAsync(bookId);

        Assert.Equal(2, vm.TotalEditions);
        Assert.Equal(3, vm.TotalCopies);
        Assert.Equal(2, vm.Book!.Editions.Count);
        Assert.Contains(vm.Book.Editions, e => e.Copies.Count == 2);
        Assert.Contains(vm.Book.Editions, e => e.Copies.Count == 1);
    }

    [Fact]
    public async Task InitializeAsync_Tags_SortedByName()
    {
        var factory = new TestDbContextFactory();

        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Author" };
            var book = new Book
            {
                Title = "Tagged",
                Works = [new Work { Title = "Tagged", Author = author }],
                Tags =
                [
                    new Tag { Name = "signed" },
                    new Tag { Name = "follow-up" },
                    new Tag { Name = "gift" },
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new BookDetailViewModel(factory);
        await vm.InitializeAsync(bookId);

        var names = vm.Book!.Tags.Select(t => t.Name).ToList();
        Assert.Equal(new[] { "follow-up", "gift", "signed" }, names);
    }
}
