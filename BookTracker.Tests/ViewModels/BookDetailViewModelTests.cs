using BookTracker.Data.Models;
using BookTracker.Web.Services.Covers;
using BookTracker.Web.ViewModels;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Integration)]
public class BookDetailViewModelTests
{
    private static BookDetailViewModel CreateVm(TestDbContextFactory factory, IBookCoverStorage? coverStorage = null) =>
        new(factory,
            coverStorage ?? Substitute.For<IBookCoverStorage>(),
            NullLogger<BookDetailViewModel>.Instance);

    [Fact]
    public async Task InitializeAsync_MissingId_MarksNotFound()
    {
        var factory = new TestDbContextFactory();
        var vm = CreateVm(factory);

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
                        WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
                        Genres = [genre],
                    }
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = CreateVm(factory);
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
                    new Work { Title = "Macbeth", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }], Series = series, SeriesOrder = 3 },
                    new Work { Title = "Hamlet", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }], Series = series, SeriesOrder = 1 },
                    new Work { Title = "Othello", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }], Series = series, SeriesOrder = 2 },
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = CreateVm(factory);
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
                Works = [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }],
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

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        Assert.Equal(2, vm.TotalEditions);
        Assert.Equal(3, vm.TotalCopies);
        Assert.Equal(2, vm.Book!.Editions.Count);
        Assert.Contains(vm.Book.Editions, e => e.Copies.Count == 2);
        Assert.Contains(vm.Book.Editions, e => e.Copies.Count == 1);
    }

    [Fact]
    public async Task SetRatingAsync_PersistsAndUpdatesCurrent()
    {
        var factory = new TestDbContextFactory();
        var bookId = await SeedSimpleBookAsync(factory, rating: 2);

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        await vm.SetRatingAsync(5);

        Assert.Equal(5, vm.CurrentRating);
        using var db = factory.CreateDbContext();
        Assert.Equal(5, db.Books.Single(b => b.Id == bookId).Rating);
    }

    [Fact]
    public async Task SetStatusAsync_PersistsAndUpdatesCurrent()
    {
        var factory = new TestDbContextFactory();
        var bookId = await SeedSimpleBookAsync(factory, status: BookStatus.Unread);

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        await vm.SetStatusAsync(BookStatus.Reading);

        Assert.Equal(BookStatus.Reading, vm.CurrentStatus);
        using var db = factory.CreateDbContext();
        Assert.Equal(BookStatus.Reading, db.Books.Single(b => b.Id == bookId).Status);
    }

    [Fact]
    public async Task SaveNotesAsync_OnlyPersistsWhenDirty()
    {
        var factory = new TestDbContextFactory();
        var bookId = await SeedSimpleBookAsync(factory, notes: "original");

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        // Not dirty yet — save should be a no-op even if CurrentNotes changed.
        vm.CurrentNotes = "changed without marking dirty";
        await vm.SaveNotesAsync();
        using (var db = factory.CreateDbContext())
        {
            Assert.Equal("original", db.Books.Single(b => b.Id == bookId).Notes);
        }

        // Now mark dirty and save.
        vm.CurrentNotes = "updated properly";
        vm.MarkNotesDirty();
        Assert.True(vm.NotesDirty);
        await vm.SaveNotesAsync();
        Assert.False(vm.NotesDirty);
        using (var db = factory.CreateDbContext())
        {
            Assert.Equal("updated properly", db.Books.Single(b => b.Id == bookId).Notes);
        }
    }

    [Fact]
    public async Task SaveNotesAsync_BlankInputPersistsAsNull()
    {
        var factory = new TestDbContextFactory();
        var bookId = await SeedSimpleBookAsync(factory, notes: "will be cleared");

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        vm.CurrentNotes = "   ";
        vm.MarkNotesDirty();
        await vm.SaveNotesAsync();

        using var db = factory.CreateDbContext();
        Assert.Null(db.Books.Single(b => b.Id == bookId).Notes);
    }

    [Fact]
    public async Task AddTagAsync_CreatesNewTagAndAssigns()
    {
        var factory = new TestDbContextFactory();
        var bookId = await SeedSimpleBookAsync(factory);

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        var added = await vm.AddTagAsync("Signed");

        Assert.NotNull(added);
        Assert.Equal("signed", added!.Name); // normalised to lowercase
        Assert.Contains(vm.CurrentTags, t => t.Name == "signed");

        using var db = factory.CreateDbContext();
        var book = db.Books.Include(b => b.Tags).Single(b => b.Id == bookId);
        Assert.Contains(book.Tags, t => t.Name == "signed");
    }

    [Fact]
    public async Task AddTagAsync_ReusesExistingTagCaseInsensitively()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            db.Tags.Add(new Tag { Name = "follow-up" }); // pre-existing tag
            var book = new Book
            {
                Title = "T",
                Works = [new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        await vm.AddTagAsync("FOLLOW-UP");

        using var db2 = factory.CreateDbContext();
        var tagCount = db2.Tags.Count(t => t.Name == "follow-up");
        Assert.Equal(1, tagCount); // not duplicated
    }

    [Fact]
    public async Task AddTagAsync_IgnoresAlreadyAssignedTag()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var tag = new Tag { Name = "gift" };
            var book = new Book
            {
                Title = "T",
                Works = [new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
                Tags = [tag],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        var second = await vm.AddTagAsync("gift");

        Assert.Null(second);
        Assert.Single(vm.CurrentTags);
    }

    [Fact]
    public async Task RemoveTagAsync_DetachesFromBookKeepsTagRow()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        int tagId;
        using (var db = factory.CreateDbContext())
        {
            var tag = new Tag { Name = "gift" };
            var book = new Book
            {
                Title = "T",
                Works = [new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
                Tags = [tag],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            tagId = tag.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        await vm.RemoveTagAsync(tagId);

        Assert.Empty(vm.CurrentTags);
        using var db2 = factory.CreateDbContext();
        var book2 = db2.Books.Include(b => b.Tags).Single(b => b.Id == bookId);
        Assert.Empty(book2.Tags);
        // Tag row itself survives — other books may still reference it.
        Assert.Equal(1, db2.Tags.Count(t => t.Id == tagId));
    }

    [Fact]
    public async Task DeleteCopyAsync_KeepsEditionWhenOtherCopiesRemain()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        int copyToDelete;
        int editionId;
        using (var db = factory.CreateDbContext())
        {
            var edition = new Edition
            {
                Isbn = "x",
                Copies = [
                    new Copy { Condition = BookCondition.Good },
                    new Copy { Condition = BookCondition.Fair },
                ],
            };
            var book = new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
                Editions = [edition],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            editionId = edition.Id;
            copyToDelete = edition.Copies[0].Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        await vm.DeleteCopyAsync(copyToDelete);

        using var db2 = factory.CreateDbContext();
        Assert.Equal(1, db2.Editions.Count(e => e.Id == editionId));
        Assert.Equal(1, db2.Copies.Count(c => c.EditionId == editionId));
    }

    [Fact]
    public async Task DeleteCopyAsync_CascadesEditionRemovalWhenLastCopy()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        int copyToDelete;
        int editionId;
        using (var db = factory.CreateDbContext())
        {
            var edition = new Edition { Isbn = "x", Copies = [new Copy { Condition = BookCondition.Good }] };
            var book = new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
                Editions = [edition],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            editionId = edition.Id;
            copyToDelete = edition.Copies[0].Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        await vm.DeleteCopyAsync(copyToDelete);

        using var db2 = factory.CreateDbContext();
        Assert.Equal(0, db2.Editions.Count(e => e.Id == editionId));
        Assert.Equal(0, db2.Copies.Count(c => c.Id == copyToDelete));
    }

    [Fact]
    public async Task SearchTagsAsync_ExcludesAlreadyAssigned()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var assigned = new Tag { Name = "signed" };
            db.Tags.Add(new Tag { Name = "follow-up" });
            db.Tags.Add(new Tag { Name = "gift" });
            var book = new Book
            {
                Title = "T",
                Works = [new Work { Title = "T", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
                Tags = [assigned],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        var results = (await vm.SearchTagsAsync("", CancellationToken.None)).ToList();

        Assert.Contains("follow-up", results);
        Assert.Contains("gift", results);
        Assert.DoesNotContain("signed", results);
    }

    private static async Task<int> SeedSimpleBookAsync(
        TestDbContextFactory factory,
        int rating = 0,
        BookStatus status = BookStatus.Unread,
        string? notes = null)
    {
        using var db = factory.CreateDbContext();
        var book = new Book
        {
            Title = "Seed",
            Status = status,
            Rating = rating,
            Notes = notes,
            Works = [new Work { Title = "Seed", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "Seed Author" }, Order = 0 }] }],
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
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
                Works = [new Work { Title = "Tagged", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }],
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

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var names = vm.Book!.Tags.Select(t => t.Name).ToList();
        Assert.Equal(new[] { "follow-up", "gift", "signed" }, names);
    }

    [Fact]
    public async Task UploadEditionCoverAsync_HappyPath_UpdatesUrlAndFlagsUserSupplied()
    {
        var factory = new TestDbContextFactory();
        int bookId, editionId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "A" };
            var book = new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }],
                Editions = [new Edition { Isbn = "1", Format = BookFormat.TradePaperback, Copies = [new Copy { Condition = BookCondition.Good }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            editionId = book.Editions[0].Id;
        }

        var storage = Substitute.For<IBookCoverStorage>();
        storage.IsEnabled.Returns(true);
        storage.UploadAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://test/book-covers/editions/" + editionId + ".jpg?v=42");

        var fileBytes = new byte[] { 0xFF, 0xD8, 0x01, 0x02, 0x03 }; // JPEG-ish header bytes
        var file = Substitute.For<IBrowserFile>();
        file.Size.Returns((long)fileBytes.Length);
        file.Name.Returns("test.jpg");
        file.ContentType.Returns("image/jpeg");
        file.OpenReadStream(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new MemoryStream(fileBytes));

        var vm = CreateVm(factory, storage);
        await vm.InitializeAsync(bookId);

        var result = await vm.UploadEditionCoverAsync(editionId, file, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://test/book-covers/editions/" + editionId + ".jpg?v=42", result.NewUrl);

        using var verifyDb = factory.CreateDbContext();
        var saved = verifyDb.Editions.Single(e => e.Id == editionId);
        Assert.Equal("https://test/book-covers/editions/" + editionId + ".jpg?v=42", saved.CoverUrl);
        Assert.True(saved.IsUserSupplied);

        // The VM snapshot should reflect the new URL after the post-upload refresh.
        var refreshedEdition = vm.Book!.Editions.Single(e => e.Id == editionId);
        Assert.Equal(saved.CoverUrl, refreshedEdition.CoverUrl);
        Assert.True(refreshedEdition.IsUserSupplied);
    }

    [Fact]
    public async Task UploadEditionCoverAsync_FileTooLarge_ReturnsFailure_AndDoesNotUpload()
    {
        var factory = new TestDbContextFactory();
        int bookId, editionId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "A" };
            var book = new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }],
                Editions = [new Edition { Format = BookFormat.TradePaperback, Copies = [new Copy { Condition = BookCondition.Good }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            editionId = book.Editions[0].Id;
        }

        var storage = Substitute.For<IBookCoverStorage>();
        storage.IsEnabled.Returns(true);

        var file = Substitute.For<IBrowserFile>();
        file.Size.Returns(BookDetailViewModel.MaxUploadBytes + 1);
        file.Name.Returns("huge.jpg");
        file.ContentType.Returns("image/jpeg");

        var vm = CreateVm(factory, storage);
        await vm.InitializeAsync(bookId);

        var result = await vm.UploadEditionCoverAsync(editionId, file, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("too large", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await storage.DidNotReceive().UploadAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadEditionCoverAsync_StorageDisabled_ReturnsFailure()
    {
        var factory = new TestDbContextFactory();
        int bookId, editionId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "A" };
            var book = new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }],
                Editions = [new Edition { Format = BookFormat.TradePaperback, Copies = [new Copy { Condition = BookCondition.Good }] }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            editionId = book.Editions[0].Id;
        }

        var storage = Substitute.For<IBookCoverStorage>();
        storage.IsEnabled.Returns(false); // configuration missing — service idle

        var file = Substitute.For<IBrowserFile>();
        file.Size.Returns(100L);

        var vm = CreateVm(factory, storage);
        await vm.InitializeAsync(bookId);

        var result = await vm.UploadEditionCoverAsync(editionId, file, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not configured", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
