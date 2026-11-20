using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Loans;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.Services.Covers;
using BookTracker.Web.ViewModels;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Integration)]
public class BookDetailViewModelTests
{
    private static BookDetailViewModel CreateVm(TestDbContextFactory factory, IBookCoverStorage? coverStorage = null) =>
        new(coverStorage ?? Substitute.For<IBookCoverStorage>(),
            NullLogger<BookDetailViewModel>.Instance,
            BuildDispatcher(factory),
            TimeProvider.System);

    // A real dispatcher backed by the application-layer DI registrations, so
    // these tests exercise the same command-resolution path as production.
    private static IDispatcher BuildDispatcher(TestDbContextFactory factory)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<BookTrackerDbContext>>(factory);
        services.AddApplicationLayer();
        return services.BuildServiceProvider().GetRequiredService<IDispatcher>();
    }

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
            var author = Author.Create("Ursula K. Le Guin");
            var genre = new Genre { Name = "Fantasy" };
            var book = TestBooks.Book(
                "A Wizard of Earthsea",
                status: BookStatus.Read,
                rating: 5,
                notes: "Gorgeous prose.",
                works:
                [
                    TestWorks.Work("A Wizard of Earthsea", author, genres: [genre])
                ]);
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
    public async Task InitializeAsync_MultiWorkBook_FlagsCompendiumAndOrdersByCaptureOrder()
    {
        var factory = new TestDbContextFactory();

        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = Author.Create("William Shakespeare");
            // Series membership is a per-Book concept — the compendium sits in the
            // collection as one installment; its Works carry no series of their own.
            var series = TestSeries.Series("Shakespeare's Plays", SeriesType.Collection);
            var book = TestBooks.Book("Complete Works", series: series);
            // Capture order deliberately differs from alphabetical (Hamlet would be
            // first if sorted by title) — the read must preserve capture order.
            // AttachWork assigns the per-book Order 0,1,2.
            book.AttachWork(TestWorks.Work("Macbeth", author));
            book.AttachWork(TestWorks.Work("Hamlet", author));
            book.AttachWork(TestWorks.Work("Othello", author));
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        Assert.False(vm.IsSingleWork);
        Assert.Equal(3, vm.Book!.Works.Count);
        Assert.Equal("Macbeth", vm.Book.Works[0].Title);   // captured first, stays first
        Assert.Equal("Hamlet", vm.Book.Works[1].Title);
        Assert.Equal("Othello", vm.Book.Works[2].Title);
    }

    [Fact]
    public async Task InitializeAsync_EditionsAndCopies_CountsAndNests()
    {
        var factory = new TestDbContextFactory();

        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = Author.Create("Terry Pratchett");
            var book = TestBooks.Book(
                "Mort",
                works: [TestWorks.Work("Mort", author)],
                editions:
                [
                    TestEditions.Edition(
                        isbn: "9780552131063",
                        format: BookFormat.MassMarketPaperback,
                        copies:
                        [
                            TestEditions.Copy(condition: BookCondition.Good),
                            TestEditions.Copy(condition: BookCondition.Fair),
                        ]),
                    TestEditions.Edition(
                        isbn: "9780061020681",
                        format: BookFormat.Hardcover,
                        copies: [TestEditions.Copy(condition: BookCondition.Fine)]),
                ]);
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
            var book = TestBooks.Book(
                "T",
                works: [TestWorks.Work("T", Author.Create("A"))]);
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
    public async Task AddTagAsync_MatchesExistingMixedCaseRow_ChipUsesStoredCasing()
    {
        // Regression (PR6b-3 review F1): a pre-existing mixed-case tag row
        // resolves case-insensitively, and the optimistic chip must show the
        // STORED casing ("Sci-Fi"), not the lowercased input — otherwise the
        // chip flips casing on the next page load.
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            db.Tags.Add(new Tag { Name = "Sci-Fi" }); // legacy mixed-case row
            var book = TestBooks.Book(
                "T",
                works: [TestWorks.Work("T", Author.Create("A"))]);
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);
        var added = await vm.AddTagAsync("sci-fi");

        Assert.NotNull(added);
        Assert.Equal("Sci-Fi", added!.Name); // stored casing, not the lowercased input
        Assert.Contains(vm.CurrentTags, t => t.Name == "Sci-Fi");

        // Resolved to the existing row, not a lowercase duplicate — a single tag
        // row survives, still in its original casing. (Count-by-name is no good
        // here: SQL Server's case-insensitive collation matches either spelling.)
        using var verify = factory.CreateDbContext();
        var stored = Assert.Single(verify.Tags);
        Assert.Equal("Sci-Fi", stored.Name);
    }

    [Fact]
    public async Task AddTagAsync_IgnoresAlreadyAssignedTag()
    {
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var tag = new Tag { Name = "gift" };
            var book = TestBooks.Book(
                "T",
                works: [TestWorks.Work("T", Author.Create("A"))],
                tags: [tag]);
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
            var book = TestBooks.Book(
                "T",
                works: [TestWorks.Work("T", Author.Create("A"))],
                tags: [tag]);
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
            var edition = TestEditions.Edition(
                isbn: "x",
                copies: [
                    TestEditions.Copy(condition: BookCondition.Good),
                    TestEditions.Copy(condition: BookCondition.Fair),
                ]);
            var book = TestBooks.Book(
                "B",
                works: [TestWorks.Work("B", Author.Create("A"))],
                editions: [edition]);
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
            var edition = TestEditions.Edition(isbn: "x", copies: [TestEditions.Copy(condition: BookCondition.Good)]);
            var book = TestBooks.Book(
                "B",
                works: [TestWorks.Work("B", Author.Create("A"))],
                editions: [edition]);
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
            var book = TestBooks.Book(
                "T",
                works: [TestWorks.Work("T", Author.Create("A"))],
                tags: [assigned]);
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
        var book = TestBooks.Book(
            "Seed",
            status: status,
            rating: rating,
            notes: notes,
            works: [TestWorks.Work("Seed", Author.Create("Seed Author"))]);
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
            var author = Author.Create("Author");
            var book = TestBooks.Book(
                "Tagged",
                works: [TestWorks.Work("Tagged", author)],
                tags:
                [
                    new Tag { Name = "signed" },
                    new Tag { Name = "follow-up" },
                    new Tag { Name = "gift" },
                ]);
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
            var author = Author.Create("A");
            var book = TestBooks.Book(
                "B",
                works: [TestWorks.Work("B", author)],
                editions: [TestEditions.Edition(isbn: "1", format: BookFormat.TradePaperback, copies: [TestEditions.Copy(condition: BookCondition.Good)])]);
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
            var author = Author.Create("A");
            var book = TestBooks.Book(
                "B",
                works: [TestWorks.Work("B", author)],
                editions: [TestEditions.Edition(format: BookFormat.TradePaperback, copies: [TestEditions.Copy(condition: BookCondition.Good)])]);
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
            var author = Author.Create("A");
            var book = TestBooks.Book(
                "B",
                works: [TestWorks.Work("B", author)],
                editions: [TestEditions.Edition(format: BookFormat.TradePaperback, copies: [TestEditions.Copy(condition: BookCondition.Good)])]);
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
        // attached — the underlying SearchWorks query already excludes by
        // bookId, so this verifies the VM is wiring that excludeBookId
        // through correctly.
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var author = Author.Create("H.P. Lovecraft");
            var cthulhu = TestWorks.Work("The Call of Cthulhu", author);
            var dunwich = TestWorks.Work("The Dunwich Horror", author);
            db.Books.Add(TestBooks.Book("Anthology A", works: [cthulhu]));
            db.Books.Add(TestBooks.Book("Anthology B", works: [dunwich]));
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
            var author = Author.Create("H.P. Lovecraft");
            var cthulhu = TestWorks.Work("The Call of Cthulhu", author);
            var dunwich = TestWorks.Work("The Dunwich Horror", author);
            db.Books.Add(TestBooks.Book("Anthology A", works: [cthulhu]));
            db.Books.Add(TestBooks.Book("Anthology B", works: [dunwich]));
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
            var author = Author.Create("Existing Author");
            var fantasy = new Genre { Name = "Fantasy" };
            db.Books.Add(TestBooks.Book(
                "Compendium",
                works: [TestWorks.Work("First story", author)]));
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
            var author = Author.Create("Author");
            db.Books.Add(TestBooks.Book(
                "Book",
                works: [TestWorks.Work("Work", author)]));
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
            var author = Author.Create("Author");
            db.Books.Add(TestBooks.Book(
                "Anthology",
                works: [TestWorks.Work("Original", author)]));
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
            var author = Author.Create("Stephen King");
            db.Books.Add(TestBooks.Book(
                "Different Seasons",
                works: [TestWorks.Work("Different Seasons", author)]));
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
            var seed = Author.Create("Seed");
            db.Books.Add(TestBooks.Book(
                "Christie Mysteries",
                works: [TestWorks.Work("Seed Work", seed)]));
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
            var king = Author.Create("Stephen King");
            var existing = TestWorks.Work("Apt Pupil", king);
            var sourceBook = TestBooks.Book("Different Seasons", works: [existing]);
            var targetBook = TestBooks.Book(
                "Four Past Midnight",
                works: [TestWorks.Work("Four Past Midnight", king)]);
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
    public async Task AttachMultipleWorksAsync_RowWithNoContributors_SetsErrorWithUserFacingMessage()
    {
        // A row with a title but no authors AND no contributors is a
        // validation failure — the VM puts the handler's message in Error
        // verbatim. Lock the message shape.
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var seed = Author.Create("Seed");
            db.Books.Add(TestBooks.Book(
                "Some Compendium",
                works: [TestWorks.Work("Seed", seed)]));
            await db.SaveChangesAsync();
            bookId = db.Books.Single().Id;
        }

        var vm = CreateVm(factory);
        await vm.InitializeAsync(bookId);

        var rows = new List<WorkFormViewModel.WorkFormInput>
        {
            new() { Title = "Untitled Story", Authors = [] },
        };

        var added = await vm.AttachMultipleWorksAsync(rows, false, false, [], []);

        Assert.Equal(0, added);
        Assert.Contains("contributor", vm.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Untitled Story", vm.Error);
    }

    [Fact]
    public async Task AttachMultipleWorksAsync_AllBlankRows_SetsErrorWithUserFacingMessage()
    {
        // Every row blank (no title, no attach) is a dialog-state error —
        // surface the "add at least one work" message rather than silently
        // closing the dialog with zero saves.
        var factory = new TestDbContextFactory();
        int bookId;
        using (var db = factory.CreateDbContext())
        {
            var seed = Author.Create("Seed");
            db.Books.Add(TestBooks.Book(
                "X",
                works: [TestWorks.Work("Seed", seed)]));
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

        var added = await vm.AttachMultipleWorksAsync(rows, false, false, [], []);

        Assert.Equal(0, added);
        Assert.Contains("at least one work", vm.Error, StringComparison.OrdinalIgnoreCase);
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
            var king = Author.Create("King");
            var existing = TestWorks.Work("Already On Book", king);
            db.Books.Add(TestBooks.Book(
                "Test Compendium",
                works: [existing]));
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
            var author = Author.Create("Lovecraft");
            var shared = TestWorks.Work("The Call of Cthulhu", author);
            var keeper = TestWorks.Work("Anchor", author);
            var current = TestBooks.Book("Compendium A", works: [keeper, shared]);
            var other = TestBooks.Book("Compendium B", works: [shared]);
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
            var author = Author.Create("Author");
            var keeper = TestWorks.Work("Anchor", author);
            var orphan = TestWorks.Work("Outlier", author);
            db.Books.Add(TestBooks.Book("Solo", works: [keeper, orphan]));
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
            var author = Author.Create("Author");
            db.Books.Add(TestBooks.Book(
                "Doomed",
                works: [TestWorks.Work("Work", author)],
                editions:
                [
                    TestEditions.Edition(
                        isbn: "9780000000001",
                        copies: [TestEditions.Copy(condition: BookCondition.Good), TestEditions.Copy(condition: BookCondition.AsNew)])
                ]));
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
            var author = Author.Create("H.P. Lovecraft");
            var work = TestWorks.Work("The Call of Cthulhu", author);
            db.Books.Add(TestBooks.Book("Anthology", works: [work]));
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

// The lending surface of the detail VM: a substituted dispatcher and a pinned
// clock. ReturnLoan's persistence is covered by the loan command tests.
[Trait("Category", TestCategories.Unit)]
public class BookDetailViewModelLoanTests
{
    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // 14:00 UTC on 19 Nov is 20 Nov in Melbourne.
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 11, 19, 14, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 11, 20);

    private const int BookId = 7;
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private static BookDetail Detail(params CopyDetail[] copies) => new(
        BookId, "Dune", BookCategory.Fiction, BookStatus.Unread, 0, null, null, new DateTime(2026, 1, 1),
        Works: [],
        Editions: [new EditionDetail(3, "9780441013593", BookFormat.Hardcover, "Hardcover", null, "", null, false, copies)],
        Tags: [],
        Series: null);

    private async Task<BookDetailViewModel> LoadedVmAsync(params CopyDetail[] copies)
    {
        _dispatcher.Query(Arg.Any<BookTracker.Application.Books.GetBookDetail>(), Arg.Any<CancellationToken>())
            .Returns(Detail(copies));
        var vm = new BookDetailViewModel(
            Substitute.For<IBookCoverStorage>(), NullLogger<BookDetailViewModel>.Instance, _dispatcher, Clock);
        await vm.InitializeAsync(BookId);
        return vm;
    }

    [Fact]
    public async Task ReturnLoanAsync_SendsReturnLoanWithToday_AndReloads()
    {
        var loan = new CopyLoanDetail(42, "Sam", Today.AddDays(-10), null);
        var vm = await LoadedVmAsync(new CopyDetail(11, BookCondition.Good, null, null, loan));
        _dispatcher.ClearReceivedCalls();

        await vm.ReturnLoanAsync(42);

        await _dispatcher.Received(1).Send(
            Arg.Is<ReturnLoan>(c => c!.LoanId == 42 && c.ReturnedOn == Today), Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).Query(
            Arg.Is<BookTracker.Application.Books.GetBookDetail>(q => q!.BookId == BookId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsOverdue_UsesThePinnedToday()
    {
        var vm = await LoadedVmAsync();

        Assert.Equal(Today, vm.Today);
        Assert.False(vm.IsOverdue(new CopyLoanDetail(1, "Sam", Today.AddDays(-5), Today)));
        Assert.True(vm.IsOverdue(new CopyLoanDetail(1, "Sam", Today.AddDays(-5), Today.AddDays(-1))));
        Assert.False(vm.IsOverdue(new CopyLoanDetail(1, "Sam", Today.AddDays(-5), null)));
    }

    [Fact]
    public async Task LendOptionFor_DescribesTheCopy()
    {
        var vm = await LoadedVmAsync(new CopyDetail(11, BookCondition.VeryGood, null, null));

        var option = vm.LendOptionFor(11);

        Assert.Equal(new LendDialogViewModel.LendCopyOption(11, "Dune", "Hardcover · 9780441013593", BookCondition.VeryGood), option);
        Assert.Null(vm.LendOptionFor(999));
    }

    private static CopyDetail Free(int id) => new(id, BookCondition.Good, null, null);

    private static CopyDetail Out(int id, int loanId, string borrower) =>
        new(id, BookCondition.Good, null, null, new CopyLoanDetail(loanId, borrower, Today.AddDays(-3), null));

    [Fact]
    public async Task NoCopies_hasNothingToLend_andNoLentLabel()
    {
        var vm = await LoadedVmAsync();

        Assert.Null(vm.FirstFreeCopyId);
        Assert.False(vm.AllCopiesLent);
        Assert.Null(vm.LentToLabel);
    }

    [Fact]
    public async Task OneFreeCopy_isTheSeed()
    {
        var vm = await LoadedVmAsync(Free(11));

        Assert.Equal(11, vm.FirstFreeCopyId);
        Assert.False(vm.AllCopiesLent);
        Assert.Null(vm.LentToLabel);
    }

    [Fact]
    public async Task SeveralCopies_seedWithTheFirstOneNotOnLoan()
    {
        var vm = await LoadedVmAsync(Out(10, 1, "Ann"), Free(11), Free(12));

        Assert.Equal(11, vm.FirstFreeCopyId);
        Assert.False(vm.AllCopiesLent);
        Assert.Null(vm.LentToLabel);
    }

    [Fact]
    public async Task EveryCopyLent_hasNoSeed_andNamesTheBorrower()
    {
        var vm = await LoadedVmAsync(Out(10, 1, "Ann"));

        Assert.Null(vm.FirstFreeCopyId);
        Assert.True(vm.AllCopiesLent);
        Assert.Equal("Lent to Ann", vm.LentToLabel);
    }

    [Fact]
    public async Task EveryCopyLentToDifferentPeople_listsEachBorrowerOnce()
    {
        var vm = await LoadedVmAsync(Out(10, 1, "Ann"), Out(11, 2, "Bob"), Out(12, 3, "Ann"));

        Assert.Equal("Lent to Ann, Bob", vm.LentToLabel);
    }

    [Fact]
    public async Task ReturnAllLoansAsync_returnsEveryOpenLoanTodayAndReloadsOnce()
    {
        var vm = await LoadedVmAsync(Out(10, 1, "Ann"), Out(11, 2, "Bob"));
        _dispatcher.ClearReceivedCalls();

        await vm.ReturnAllLoansAsync();

        await _dispatcher.Received(1).Send(
            Arg.Is<ReturnLoan>(c => c!.LoanId == 1 && c.ReturnedOn == Today), Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).Send(
            Arg.Is<ReturnLoan>(c => c!.LoanId == 2 && c.ReturnedOn == Today), Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).Query(
            Arg.Any<BookTracker.Application.Books.GetBookDetail>(), Arg.Any<CancellationToken>());
    }
}
