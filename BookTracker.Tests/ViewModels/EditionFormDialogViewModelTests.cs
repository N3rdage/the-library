using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

public class EditionFormDialogViewModelTests
{
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();

    [Fact]
    public async Task InitializeForAdd_SetsIsNewAndBookId()
    {
        var factory = new TestDbContextFactory();
        var vm = new EditionFormDialogViewModel(factory, _lookup);

        await vm.InitializeForAddAsync(42);

        Assert.True(vm.IsNew);
        Assert.Equal(42, vm.BookId);
        Assert.Null(vm.EditionId);
    }

    [Fact]
    public async Task InitializeForEditAsync_MissingId_MarksNotFound()
    {
        var factory = new TestDbContextFactory();
        var vm = new EditionFormDialogViewModel(factory, _lookup);

        await vm.InitializeForEditAsync(999);

        Assert.True(vm.NotFound);
    }

    [Fact]
    public async Task InitializeForEditAsync_LoadsEditionFields()
    {
        var factory = new TestDbContextFactory();
        int editionId;
        using (var db = factory.CreateDbContext())
        {
            var publisher = new Publisher { Name = "Corgi" };
            var edition = new Edition
            {
                Isbn = "9780552131063",
                Format = BookFormat.MassMarketPaperback,
                Publisher = publisher,
                DatePrinted = new DateOnly(1987, 1, 1),
                DatePrintedPrecision = DatePrecision.Year,
                Copies = [new Copy { Condition = BookCondition.Good }],
            };
            db.Books.Add(new Book
            {
                Title = "Mort",
                Works = [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "Pratchett" }, Order = 0 }] }],
                Editions = [edition],
            });
            await db.SaveChangesAsync();
            editionId = edition.Id;
        }

        var vm = new EditionFormDialogViewModel(factory, _lookup);
        await vm.InitializeForEditAsync(editionId);

        Assert.False(vm.IsNew);
        Assert.False(vm.NotFound);
        Assert.Equal("9780552131063", vm.Isbn);
        Assert.Equal(BookFormat.MassMarketPaperback, vm.Format);
        Assert.Equal("Corgi", vm.Publisher);
        Assert.Equal("1987", vm.FirstPublishedOrPrintedDate);
    }

    [Fact]
    public async Task SaveAsync_Add_CreatesEditionAndFirstCopy()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var book = new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new EditionFormDialogViewModel(factory, _lookup);
        await vm.InitializeForAddAsync(bookId);
        vm.Isbn = "9780552131063";
        vm.Format = BookFormat.MassMarketPaperback;
        vm.Publisher = "Corgi";
        vm.FirstPublishedOrPrintedDate = "1987";
        vm.FirstCopyCondition = BookCondition.VeryGood;
        var id = await vm.SaveAsync();

        Assert.NotNull(id);
        using var db2 = factory.CreateDbContext();
        var edition = db2.Editions.Include(e => e.Copies).Include(e => e.Publisher).Single(e => e.Id == id);
        Assert.Equal("9780552131063", edition.Isbn);
        Assert.Equal(BookFormat.MassMarketPaperback, edition.Format);
        Assert.Equal("Corgi", edition.Publisher!.Name);
        Assert.Single(edition.Copies);
        Assert.Equal(BookCondition.VeryGood, edition.Copies[0].Condition);
    }

    [Fact]
    public async Task SaveAsync_Add_NoIsbnPersistsAsNull()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var book = new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new EditionFormDialogViewModel(factory, _lookup);
        await vm.InitializeForAddAsync(bookId);
        vm.Isbn = "   ";
        vm.Format = BookFormat.Hardcover;
        var id = await vm.SaveAsync();

        Assert.NotNull(id);
        using var db2 = factory.CreateDbContext();
        var edition = db2.Editions.Single(e => e.Id == id);
        Assert.Null(edition.Isbn);
    }

    [Fact]
    public async Task SaveAsync_Add_ReusesExistingPublisherByName()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        int existingPublisherId;
        using (var db = factory.CreateDbContext())
        {
            var corgi = new Publisher { Name = "Corgi" };
            db.Publishers.Add(corgi);
            var book = new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            existingPublisherId = corgi.Id;
        }

        var vm = new EditionFormDialogViewModel(factory, _lookup);
        await vm.InitializeForAddAsync(bookId);
        vm.Isbn = "x";
        vm.Publisher = "Corgi";
        var id = await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        var edition = db2.Editions.Single(e => e.Id == id);
        Assert.Equal(existingPublisherId, edition.PublisherId);
        Assert.Equal(1, db2.Publishers.Count());
    }

    [Fact]
    public async Task SaveAsync_Edit_UpdatesFieldsInPlace()
    {
        var factory = new TestDbContextFactory();
        int editionId;
        using (var db = factory.CreateDbContext())
        {
            var seedEdition = new Edition
            {
                Isbn = "old",
                Format = BookFormat.Hardcover,
                Copies = [new Copy { Condition = BookCondition.Good }],
            };
            db.Books.Add(new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
                Editions = [seedEdition],
            });
            await db.SaveChangesAsync();
            editionId = seedEdition.Id;
        }

        var vm = new EditionFormDialogViewModel(factory, _lookup);
        await vm.InitializeForEditAsync(editionId);
        vm.Isbn = "9780000000001";
        vm.Format = BookFormat.TradePaperback;
        vm.Publisher = "NewPub";
        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        var edition = db2.Editions.Include(e => e.Publisher).Include(e => e.Copies).Single(e => e.Id == editionId);
        Assert.Equal("9780000000001", edition.Isbn);
        Assert.Equal(BookFormat.TradePaperback, edition.Format);
        Assert.Equal("NewPub", edition.Publisher!.Name);
        Assert.Single(edition.Copies); // Edit doesn't duplicate the copy
    }

    [Fact]
    public async Task SearchPublishersAsync_MatchesSubstring()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            db.Publishers.AddRange(
                new Publisher { Name = "Corgi" },
                new Publisher { Name = "Gollancz" },
                new Publisher { Name = "Orbit" });
            var book = new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = new EditionFormDialogViewModel(factory, _lookup);
        await vm.InitializeForAddAsync(bookId);
        var results = (await vm.SearchPublishersAsync("or", CancellationToken.None)).ToList();

        Assert.Contains("Corgi", results);
        Assert.Contains("Orbit", results);
        Assert.DoesNotContain("Gollancz", results);
    }

    [Fact]
    public async Task LookupAsync_PopulatesEmptyFieldsFromResult()
    {
        var factory = new TestDbContextFactory();
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780552131063",
                Title: "Mort",
                Subtitle: null,
                Author: "Pratchett",
                Publisher: "Corgi",
                GenreCandidates: [],
                DatePrinted: new DateOnly(1987, 11, 12),
                CoverUrl: "https://example.com/mort.jpg",
                Source: "OpenLibrary",
                Format: BookFormat.MassMarketPaperback,
                DatePrintedPrecision: DatePrecision.Day));

        var vm = new EditionFormDialogViewModel(factory, _lookup);
        await vm.InitializeForAddAsync(1);
        vm.Isbn = "9780552131063";
        await vm.LookupAsync();

        Assert.Equal("Corgi", vm.Publisher);
        Assert.Equal("https://example.com/mort.jpg", vm.CoverUrl);
        Assert.Equal(BookFormat.MassMarketPaperback, vm.Format);
        Assert.NotEmpty(vm.FirstPublishedOrPrintedDate);
    }
}
