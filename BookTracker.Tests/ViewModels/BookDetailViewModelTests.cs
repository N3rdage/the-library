using BookTracker.Data.Models;
using BookTracker.Web.Services;
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
            new WorkSearchService(factory),
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
    public async Task SetStatusAsync_ReferenceStatus_PersistsAndRoundTrips()
    {
        // Dictionaries / reference rows opt out of the Unread/Reading/Read
        // arc — a fourth enum value (Reference) introduced 2026-05-24.
        // Locks the column round-trip so a future enum-shape change
        // (e.g. dropping the value, renaming) surfaces here.
        var factory = new TestDbContextFactory();
        var bookId = await SeedSimpleBookAsync(factory, status: BookStatus.Unread);

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        await vm.SetStatusAsync(BookStatus.Reference);

        Assert.Equal(BookStatus.Reference, vm.CurrentStatus);
        using var db = factory.CreateDbContext();
        Assert.Equal(BookStatus.Reference, db.Books.Single(b => b.Id == bookId).Status);
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

    [Fact]
    public async Task SearchAttachableWorksAsync_ExcludesWorksAlreadyOnThisBook()
    {
        // The dialog's search shouldn't offer up Works the user has already
        // attached — the underlying WorkSearchService already excludes by
        // bookId, so this verifies the VM is wiring that excludeBookId
        // through correctly.
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "H.P. Lovecraft" };
            var cthulhu = new Work
            {
                Title = "The Call of Cthulhu",
                WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
            };
            var dunwich = new Work
            {
                Title = "The Dunwich Horror",
                WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
            };
            db.Books.Add(new Book { Title = "Anthology A", Works = [cthulhu] });
            db.Books.Add(new Book { Title = "Anthology B", Works = [dunwich] });
            await db.SaveChangesAsync();
            bookId = db.Books.Single(b => b.Title == "Anthology A").Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var results = await vm.SearchAttachableWorksAsync("the", CancellationToken.None);

        // "The Call of Cthulhu" is already on Anthology A → filtered out.
        // "The Dunwich Horror" is on Anthology B → eligible.
        Assert.Single(results);
        Assert.Equal("The Dunwich Horror", results[0].Title);
    }

    [Fact]
    public async Task AttachExistingWorkAsync_AddsWorkToBookAndRefreshesSnapshot()
    {
        // The HP Lovecraft "complete works" case — attach a Work that
        // already exists elsewhere in the library to the current Book
        // without duplicating it.
        var factory = new TestDbContextFactory();
        int bookId, dunwichId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "H.P. Lovecraft" };
            var cthulhu = new Work
            {
                Title = "The Call of Cthulhu",
                WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
            };
            var dunwich = new Work
            {
                Title = "The Dunwich Horror",
                WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
            };
            db.Books.Add(new Book { Title = "Anthology A", Works = [cthulhu] });
            db.Books.Add(new Book { Title = "Anthology B", Works = [dunwich] });
            await db.SaveChangesAsync();
            bookId = db.Books.Single(b => b.Title == "Anthology A").Id;
            dunwichId = dunwich.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        Assert.Single(vm.Book!.Works);

        var attachedTitle = await vm.AttachExistingWorkAsync(dunwichId);

        Assert.Equal("The Dunwich Horror", attachedTitle);
        // VM snapshot refreshed inline → page sees the new Work immediately.
        Assert.Equal(2, vm.Book!.Works.Count);
        Assert.Contains(vm.Book.Works, w => w.Id == dunwichId);

        // Underlying DB: Work is on BOTH books now, no duplicate Work row.
        using var verifyDb = factory.CreateDbContext();
        var attached = await verifyDb.Works.Include(w => w.Books).FirstAsync(w => w.Id == dunwichId);
        Assert.Equal(2, attached.Books.Count);
    }

    [Fact]
    public async Task CreateAndAttachWorkAsync_AddsNewWorkToBookWithSuppliedFields()
    {
        // PR 6 — the AddWorkDialog's "Save (create new)" path lands here.
        // Verifies the new Work is wired into Book.Works with the typed
        // fields and a freshly-created Author row.
        var factory = new TestDbContextFactory();
        int bookId, fantasyId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Existing Author" };
            var fantasy = new Genre { Name = "Fantasy" };
            db.Books.Add(new Book
            {
                Title = "Compendium",
                Works = [new Work
                {
                    Title = "First story",
                    WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
                }]
            });
            db.Genres.Add(fantasy);
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
            fantasyId = fantasy.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var workId = await vm.CreateAndAttachWorkAsync(
            title: "Newly captured story",
            authorNames: ["Newly Captured Author"],
            subtitle: "A subtitle",
            firstPublishedDate: "1934",
            genreIds: [fantasyId]);

        Assert.NotNull(workId);
        // Snapshot refreshed inline → page sees both works.
        Assert.Equal(2, vm.Book!.Works.Count);

        using var verify = factory.CreateDbContext();
        var work = await verify.Works
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(w => w.Genres)
            .FirstAsync(w => w.Id == workId);
        Assert.Equal("Newly captured story", work.Title);
        Assert.Equal("A subtitle", work.Subtitle);
        Assert.Equal(new DateOnly(1934, 1, 1), work.FirstPublishedDate);
        Assert.Equal(DatePrecision.Year, work.FirstPublishedDatePrecision);
        Assert.Equal("Newly Captured Author", work.WorkAuthors.Single().Author.Name);
        Assert.Contains(work.Genres, g => g.Id == fantasyId);
    }

    [Fact]
    public async Task CreateAndAttachWorkAsync_NoContributorsOfAnyRole_ReturnsNullWithoutSaving()
    {
        // Defensive guard — the dialog's Save button is disabled while
        // both contributor lists are empty, but the VM enforces it
        // independently. Editor-only is legal (see the editor-only test
        // below); the no-contributor-at-all case still no-ops.
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Author" };
            db.Books.Add(new Book
            {
                Title = "Book",
                Works = [new Work { Title = "Work", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }]
            });
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var workId = await vm.CreateAndAttachWorkAsync(
            title: "Title only",
            authorNames: [],
            subtitle: null,
            firstPublishedDate: null,
            genreIds: [],
            contributors: []);

        Assert.Null(workId);
        Assert.Single(vm.Book!.Works);
    }

    [Fact]
    public async Task CreateAndAttachWorkAsync_EditorOnly_NoAuthors_Saves()
    {
        // Editor-only Work attaches and persists — the row carries a
        // WorkAuthor with Role=Editor and no Author-role row.
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Author" };
            db.Books.Add(new Book
            {
                Title = "Anthology",
                Works = [new Work { Title = "Original", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }]
            });
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var workId = await vm.CreateAndAttachWorkAsync(
            title: "Edited Companion",
            authorNames: [],
            subtitle: null,
            firstPublishedDate: null,
            genreIds: [],
            contributors: [new ContributorEntry { Name = "Jane Editor", Role = AuthorRole.Editor }]);

        Assert.NotNull(workId);

        using var verify = factory.CreateDbContext();
        var work = await verify.Works
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstAsync(w => w.Id == workId);
        var sole = Assert.Single(work.WorkAuthors);
        Assert.Equal(AuthorRole.Editor, sole.Role);
        Assert.Equal("Jane Editor", sole.Author.Name);
    }

    [Fact]
    public async Task AttachMultipleWorksAsync_NewWorks_CreatesAndAttachesAll()
    {
        // The motivating case: a Book captured via Bulk Add as a single
        // Work turns out to be a 4-story anthology. AttachMultipleWorks
        // takes the four titles + per-row authors and creates four new
        // Works attached to the existing Book in one save.
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Stephen King" };
            db.Books.Add(new Book
            {
                Title = "Different Seasons",
                Works = [new Work { Title = "Different Seasons", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }],
            });
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var rows = new List<WorkFormViewModel.WorkFormInput>
        {
            new() { Title = "Rita Hayworth and Shawshank Redemption", Authors = ["Stephen King"] },
            new() { Title = "Apt Pupil", Authors = ["Stephen King"] },
            new() { Title = "The Body", Authors = ["Stephen King"] },
            new() { Title = "The Breathing Method", Authors = ["Stephen King"] },
        };

        var added = await vm.AttachMultipleWorksAsync(rows,
            singleAuthor: false, singleGenre: false,
            sharedAuthors: [], sharedGenreIds: []);

        Assert.Equal(4, added);
        using var verify = factory.CreateDbContext();
        var book = await verify.Books.Include(b => b.Works).FirstAsync(b => b.Id == bookId);
        Assert.Equal(5, book.Works.Count); // original + 4 new
        Assert.Contains(book.Works, w => w.Title == "Apt Pupil");
    }

    [Fact]
    public async Task AttachMultipleWorksAsync_SingleAuthorMode_AppliesSharedAuthors()
    {
        // Single-Author mode is the "13 stories, all King" path — the
        // shared author list applies to every new row at save time.
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var seed = new Author { Name = "Seed" };
            db.Books.Add(new Book
            {
                Title = "Christie Mysteries",
                Works = [new Work { Title = "Seed Work", WorkAuthors = [new WorkAuthor { Author = seed, Order = 0 }] }],
            });
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var rows = new List<WorkFormViewModel.WorkFormInput>
        {
            new() { Title = "And Then There Were None", Authors = [] },
            new() { Title = "Murder on the Orient Express", Authors = [] },
        };

        var added = await vm.AttachMultipleWorksAsync(rows,
            singleAuthor: true, singleGenre: false,
            sharedAuthors: ["Agatha Christie"], sharedGenreIds: []);

        Assert.Equal(2, added);
        using var verify = factory.CreateDbContext();
        var book = await verify.Books
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstAsync(b => b.Id == bookId);
        var christies = book.Works.Where(w => w.Title != "Seed Work").ToList();
        Assert.All(christies, w =>
        {
            Assert.Single(w.WorkAuthors);
            Assert.Equal("Agatha Christie", w.WorkAuthors[0].Author.Name);
        });
    }

    [Fact]
    public async Task AttachMultipleWorksAsync_MixedNewAndExisting_AttachesExistingByIdAndCreatesNew()
    {
        // Compendium overlap — Drew is adding "Four Past Midnight" which
        // contains "The Library Policeman" (new) and also wants to attach
        // the existing "Apt Pupil" Work (from Different Seasons). Mixed
        // rows must work in one save.
        var factory = new TestDbContextFactory();
        int targetBookId, existingWorkId;
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var existing = new Work
            {
                Title = "Apt Pupil",
                WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }],
            };
            var sourceBook = new Book { Title = "Different Seasons", Works = [existing] };
            var targetBook = new Book
            {
                Title = "Four Past Midnight",
                Works = [new Work { Title = "Four Past Midnight", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }],
            };
            db.Books.AddRange(sourceBook, targetBook);
            await db.SaveChangesAsync();
            targetBookId = targetBook.Id;
            existingWorkId = existing.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(targetBookId);

        var rows = new List<WorkFormViewModel.WorkFormInput>
        {
            new() { Title = "The Library Policeman", Authors = ["Stephen King"] },
            new() { Title = "Apt Pupil", AttachedWorkId = existingWorkId, AttachedWorkAuthor = "Stephen King" },
        };

        var added = await vm.AttachMultipleWorksAsync(rows,
            singleAuthor: false, singleGenre: false,
            sharedAuthors: [], sharedGenreIds: []);

        Assert.Equal(2, added);
        using var verify = factory.CreateDbContext();
        var book = await verify.Books.Include(b => b.Works).FirstAsync(b => b.Id == targetBookId);
        Assert.Equal(3, book.Works.Count); // original + 1 new + 1 attached
        Assert.Contains(book.Works, w => w.Id == existingWorkId);
        Assert.Contains(book.Works, w => w.Title == "The Library Policeman");
    }

    [Fact]
    public async Task AttachMultipleWorksAsync_RowWithNoContributors_ThrowsWithUserFacingMessage()
    {
        // A row with a title but no authors AND no contributors is a
        // validation failure — the dialog's catch surfaces the .Message
        // verbatim. Lock the message shape.
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var seed = new Author { Name = "Seed" };
            db.Books.Add(new Book
            {
                Title = "Some Compendium",
                Works = [new Work { Title = "Seed", WorkAuthors = [new WorkAuthor { Author = seed, Order = 0 }] }],
            });
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var rows = new List<WorkFormViewModel.WorkFormInput>
        {
            new() { Title = "Untitled Story", Authors = [] },
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            vm.AttachMultipleWorksAsync(rows, false, false, [], []));
        Assert.Contains("contributor", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Untitled Story", ex.Message);
    }

    [Fact]
    public async Task AttachMultipleWorksAsync_AllBlankRows_ThrowsWithUserFacingMessage()
    {
        // Every row blank (no title, no attach) is a dialog-state error —
        // surface the "add at least one work" message rather than silently
        // closing the dialog with zero saves.
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var seed = new Author { Name = "Seed" };
            db.Books.Add(new Book
            {
                Title = "X",
                Works = [new Work { Title = "Seed", WorkAuthors = [new WorkAuthor { Author = seed, Order = 0 }] }],
            });
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var rows = new List<WorkFormViewModel.WorkFormInput>
        {
            new() { Title = null, Authors = [] },
            new() { Title = "   ", Authors = [] },
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            vm.AttachMultipleWorksAsync(rows, false, false, [], []));
        Assert.Contains("at least one work", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AttachMultipleWorksAsync_AlreadyAttachedExistingWork_SilentlySkipsAndCountsOthers()
    {
        // If the user picks an existing Work that's already on this Book
        // (the dialog's search filter should normally prevent this, but
        // stale state can leak through), the row is skipped silently —
        // attachedCount reflects only newly-attached rows.
        var factory = new TestDbContextFactory();
        int bookId, existingWorkId;
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "King" };
            var existing = new Work
            {
                Title = "Already On Book",
                WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }],
            };
            db.Books.Add(new Book
            {
                Title = "Test Compendium",
                Works = [existing],
            });
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
            existingWorkId = existing.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var rows = new List<WorkFormViewModel.WorkFormInput>
        {
            new() { Title = "Already On Book", AttachedWorkId = existingWorkId, AttachedWorkAuthor = "King" },
            new() { Title = "A New One", Authors = ["King"] },
        };

        var added = await vm.AttachMultipleWorksAsync(rows, false, false, [], []);

        Assert.Equal(1, added); // only the new one counted
        using var verify = factory.CreateDbContext();
        var book = await verify.Books.Include(b => b.Works).FirstAsync(b => b.Id == bookId);
        Assert.Equal(2, book.Works.Count); // existing + new
    }

    [Fact]
    public async Task RemoveWorkFromBookAsync_WorkOnMultipleBooks_OnlyDetachesFromCurrent()
    {
        // Shared Work between two Books (Drew's Lovecraft case) — removing
        // it from one Book must keep the other Book's join intact.
        var factory = new TestDbContextFactory();
        int currentBookId, otherBookId, workId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Lovecraft" };
            var shared = new Work
            {
                Title = "The Call of Cthulhu",
                WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
            };
            var keeper = new Work
            {
                Title = "Anchor",
                WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
            };
            var current = new Book { Title = "Compendium A", Works = [keeper, shared] };
            var other = new Book { Title = "Compendium B", Works = [shared] };
            db.Books.AddRange(current, other);
            await db.SaveChangesAsync();
            currentBookId = current.Id;
            otherBookId = other.Id;
            workId = shared.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(currentBookId);

        var removed = await vm.RemoveWorkFromBookAsync(workId);

        Assert.Equal("The Call of Cthulhu", removed);
        // Current book loses the work — only the anchor remains.
        Assert.Single(vm.Book!.Works);
        // Other book still has the work (Work row preserved).
        using var verify = factory.CreateDbContext();
        var work = await verify.Works.Include(w => w.Books).FirstOrDefaultAsync(w => w.Id == workId);
        Assert.NotNull(work);
        Assert.Single(work!.Books);
        Assert.Equal(otherBookId, work.Books[0].Id);
    }

    [Fact]
    public async Task RemoveWorkFromBookAsync_WorkOnOnlyThisBook_DeletesWorkOutright()
    {
        // No other Books reference the Work → orphan after detach. Delete
        // outright to avoid Work-noise data.
        var factory = new TestDbContextFactory();
        int bookId, keeperWorkId, orphanWorkId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Author" };
            var keeper = new Work { Title = "Anchor", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
            var orphan = new Work { Title = "Outlier", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
            db.Books.Add(new Book { Title = "Solo", Works = [keeper, orphan] });
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
            keeperWorkId = keeper.Id;
            orphanWorkId = orphan.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var removed = await vm.RemoveWorkFromBookAsync(orphanWorkId);

        Assert.Equal("Outlier", removed);
        Assert.Single(vm.Book!.Works);
        using var verify = factory.CreateDbContext();
        Assert.False(await verify.Works.AnyAsync(w => w.Id == orphanWorkId));
        Assert.True(await verify.Works.AnyAsync(w => w.Id == keeperWorkId));
    }

    [Fact]
    public async Task DeleteBookAsync_SoftDeletesBookAndCascadesEditionsCopies()
    {
        // Soft-delete shape: the Book row stays as a tombstone (DeletedAt
        // set), invisible to normal queries via the global HasQueryFilter.
        // Editions + Copies hard-removed at the same save (cascade) so
        // user-visible behaviour matches the old hard-delete — only the
        // husk row survives for the catalog snapshot's deletedIds[].
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "Author" };
            db.Books.Add(new Book
            {
                Title = "Doomed",
                Works = [new Work { Title = "Work", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }],
                Editions =
                [
                    new Edition
                    {
                        Isbn = "9780000000001",
                        Copies = [new Copy { Condition = BookCondition.Good }, new Copy { Condition = BookCondition.AsNew }],
                    }
                ]
            });
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var ok = await vm.DeleteBookAsync();

        Assert.True(ok);
        using var verify = factory.CreateDbContext();
        // Normal queries see the book as gone — query filter hides it.
        Assert.Empty(verify.Books);
        // Children hard-removed via cascade.
        Assert.Empty(verify.Editions);
        Assert.Empty(verify.Copies);
        // The husk row survives with DeletedAt set so the snapshot
        // delta path can emit it in deletedIds[].
        var husk = await verify.Books.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == bookId);
        Assert.NotNull(husk);
        Assert.NotNull(husk!.DeletedAt);
    }

    [Fact]
    public async Task AttachExistingWorkAsync_AlreadyAttached_ReturnsNullAndDoesNotDoubleAdd()
    {
        // Defensive guard against stale dialog state (search filter normally
        // prevents this — but a fast double-click in the dialog could race).
        var factory = new TestDbContextFactory();
        int bookId, workId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "H.P. Lovecraft" };
            var work = new Work
            {
                Title = "The Call of Cthulhu",
                WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
            };
            db.Books.Add(new Book { Title = "Anthology", Works = [work] });
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
            workId = work.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var result = await vm.AttachExistingWorkAsync(workId);

        Assert.Null(result);
        Assert.Single(vm.Book!.Works);
    }
}
