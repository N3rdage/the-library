using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the Books command handlers against the process-shared
// SQL Server container. Each `new TestDbContextFactory()` wipes + reseeds, so
// the factory instance is the clean-state harness AND the IDbContextFactory the
// handler runs against (no re-wipe between the seed and the act — the wipe only
// happens in the factory ctor).
[Trait("Category", TestCategories.Integration)]
public class BookCommandHandlersTests
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<int> SeedBookAsync()
    {
        await using var db = _factory.CreateDbContext();
        var book = new Book { Title = "Dune" };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    private async Task<int> SeedEditionAsync(int bookId, BookCondition firstCopy = BookCondition.Good) =>
        await new AddEditionToBookHandler(_factory).HandleAsync(
            new AddEditionToBook(bookId, null, BookFormat.Hardcover, null, DatePrecision.Day, null, null, firstCopy));

    [Fact]
    public async Task RateBook_persists()
    {
        var id = await SeedBookAsync();
        await new RateBookHandler(_factory).HandleAsync(new RateBook(id, 5));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(5, (await db.Books.FindAsync(id))!.Rating);
    }

    [Fact]
    public async Task RateBook_invalidRating_throwsDomainRule_andDoesNotPersist()
    {
        var id = await SeedBookAsync();
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new RateBookHandler(_factory).HandleAsync(new RateBook(id, 9)));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(0, (await db.Books.FindAsync(id))!.Rating);
    }

    [Fact]
    public async Task RateBook_missingBook_throwsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new RateBookHandler(_factory).HandleAsync(new RateBook(424242, 3)));
    }

    [Fact]
    public async Task MarkBookRead_setsStatusRatingAndNotes_inOneSave()
    {
        var id = await SeedBookAsync();
        await new MarkBookReadHandler(_factory).HandleAsync(new MarkBookRead(id, 4, "great read"));

        await using var db = _factory.CreateDbContext();
        var book = await db.Books.FindAsync(id);
        Assert.Equal(BookStatus.Read, book!.Status);
        Assert.Equal(4, book.Rating);
        Assert.Equal("great read", book.Notes);
    }

    [Fact]
    public async Task SetBookStatus_persists()
    {
        var id = await SeedBookAsync();
        await new SetBookStatusHandler(_factory).HandleAsync(new SetBookStatus(id, BookStatus.Reading));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(BookStatus.Reading, (await db.Books.FindAsync(id))!.Status);
    }

    [Fact]
    public async Task UpdateBookNotes_trimsAndPersists()
    {
        var id = await SeedBookAsync();
        await new UpdateBookNotesHandler(_factory).HandleAsync(new UpdateBookNotes(id, "  great  "));

        await using var db = _factory.CreateDbContext();
        Assert.Equal("great", (await db.Books.FindAsync(id))!.Notes);
    }

    [Fact]
    public async Task UpdateBookDetails_persists()
    {
        var id = await SeedBookAsync();
        await new UpdateBookDetailsHandler(_factory).HandleAsync(
            new UpdateBookDetails(id, "New Title", BookCategory.NonFiction, "http://c"));

        await using var db = _factory.CreateDbContext();
        var book = await db.Books.FindAsync(id);
        Assert.Equal("New Title", book!.Title);
        Assert.Equal(BookCategory.NonFiction, book.Category);
    }

    [Fact]
    public async Task AddEditionToBook_createsEditionWithFirstCopy_andResolvesPublisher()
    {
        var id = await SeedBookAsync();
        var editionId = await new AddEditionToBookHandler(_factory).HandleAsync(
            new AddEditionToBook(id, "9781234567890", BookFormat.Hardcover, null, DatePrecision.Day, "Penguin", null, BookCondition.Fine));

        await using var db = _factory.CreateDbContext();
        var edition = await db.Editions.Include(e => e.Copies).Include(e => e.Publisher).FirstAsync(e => e.Id == editionId);
        Assert.Equal(id, edition.BookId);
        Assert.Single(edition.Copies);
        Assert.Equal(BookCondition.Fine, edition.Copies[0].Condition);
        Assert.Equal("Penguin", edition.Publisher!.Name);
    }

    [Fact]
    public async Task AddEditionToBook_reusesExistingPublisher()
    {
        var id = await SeedBookAsync();
        var handler = new AddEditionToBookHandler(_factory);
        await handler.HandleAsync(new AddEditionToBook(id, null, BookFormat.Hardcover, null, DatePrecision.Day, "Tor", null, BookCondition.Good));
        await handler.HandleAsync(new AddEditionToBook(id, null, BookFormat.TradePaperback, null, DatePrecision.Day, "Tor", null, BookCondition.Good));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(1, await db.Publishers.CountAsync(p => p.Name == "Tor"));
    }

    [Fact]
    public async Task AddCopyToEdition_addsSecondCopy()
    {
        var id = await SeedBookAsync();
        var editionId = await SeedEditionAsync(id);

        var copyId = await new AddCopyToEditionHandler(_factory).HandleAsync(
            new AddCopyToEdition(editionId, BookCondition.Poor, null, "spare"));

        await using var db = _factory.CreateDbContext();
        var copies = await db.Copies.Where(c => c.EditionId == editionId).ToListAsync();
        Assert.Equal(2, copies.Count);
        Assert.Contains(copies, c => c.Id == copyId && c.Condition == BookCondition.Poor);
    }

    [Fact]
    public async Task UpdateEdition_persistsFieldsAndPublisher()
    {
        var id = await SeedBookAsync();
        var editionId = await SeedEditionAsync(id);

        await new UpdateEditionHandler(_factory).HandleAsync(
            new UpdateEdition(editionId, "9780000000002", BookFormat.MassMarketPaperback, null, DatePrecision.Day, "Gollancz", "http://x"));

        await using var db = _factory.CreateDbContext();
        var edition = await db.Editions.Include(e => e.Publisher).FirstAsync(e => e.Id == editionId);
        Assert.Equal(BookFormat.MassMarketPaperback, edition.Format);
        Assert.Equal("9780000000002", edition.Isbn);
        Assert.Equal("Gollancz", edition.Publisher!.Name);
    }

    [Fact]
    public async Task UpdateCopy_persists()
    {
        var id = await SeedBookAsync();
        var editionId = await SeedEditionAsync(id);
        int copyId;
        await using (var seed = _factory.CreateDbContext())
            copyId = (await seed.Copies.FirstAsync(c => c.EditionId == editionId)).Id;

        await new UpdateCopyHandler(_factory).HandleAsync(new UpdateCopy(copyId, BookCondition.Fair, null, "worn"));

        await using var db = _factory.CreateDbContext();
        var copy = await db.Copies.FindAsync(copyId);
        Assert.Equal(BookCondition.Fair, copy!.Condition);
        Assert.Equal("worn", copy.Notes);
    }

    [Fact]
    public async Task DeleteCopy_lastCopy_removesEdition()
    {
        var id = await SeedBookAsync();
        var editionId = await SeedEditionAsync(id);
        int copyId;
        await using (var seed = _factory.CreateDbContext())
            copyId = (await seed.Copies.FirstAsync(c => c.EditionId == editionId)).Id;

        await new DeleteCopyHandler(_factory).HandleAsync(new DeleteCopy(id, copyId));

        await using var db = _factory.CreateDbContext();
        Assert.Null(await db.Copies.FindAsync(copyId));
        Assert.Null(await db.Editions.FindAsync(editionId));
    }

    [Fact]
    public async Task DeleteCopy_nonLastCopy_keepsEdition()
    {
        var id = await SeedBookAsync();
        var editionId = await SeedEditionAsync(id);
        await new AddCopyToEditionHandler(_factory).HandleAsync(new AddCopyToEdition(editionId, BookCondition.Good, null, null));
        int firstCopyId;
        await using (var seed = _factory.CreateDbContext())
            firstCopyId = (await seed.Copies.Where(c => c.EditionId == editionId).OrderBy(c => c.Id).FirstAsync()).Id;

        await new DeleteCopyHandler(_factory).HandleAsync(new DeleteCopy(id, firstCopyId));

        await using var db = _factory.CreateDbContext();
        Assert.NotNull(await db.Editions.FindAsync(editionId));
        Assert.Equal(1, await db.Copies.CountAsync(c => c.EditionId == editionId));
    }

    [Fact]
    public async Task DeleteCopy_copyNotOnBook_throwsDomainRule()
    {
        var id = await SeedBookAsync();
        await SeedEditionAsync(id);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new DeleteCopyHandler(_factory).HandleAsync(new DeleteCopy(id, 999999)));
    }

    [Fact]
    public async Task DeleteBook_softDeletes_andHardRemovesEditions()
    {
        var id = await SeedBookAsync();
        var editionId = await SeedEditionAsync(id);

        await new DeleteBookHandler(_factory).HandleAsync(new DeleteBook(id));

        await using var db = _factory.CreateDbContext();
        Assert.Null(await db.Books.FirstOrDefaultAsync(b => b.Id == id)); // hidden by the global query filter
        var tomb = await db.Books.IgnoreQueryFilters().FirstAsync(b => b.Id == id);
        Assert.NotNull(tomb.DeletedAt);
        Assert.Null(await db.Editions.FindAsync(editionId));
    }

    [Fact]
    public async Task SetEditionCover_persists()
    {
        var id = await SeedBookAsync();
        var editionId = await SeedEditionAsync(id);

        await new SetEditionCoverHandler(_factory).HandleAsync(new SetEditionCover(editionId, "http://cover", true));

        await using var db = _factory.CreateDbContext();
        var edition = await db.Editions.FindAsync(editionId);
        Assert.Equal("http://cover", edition!.CoverUrl);
        Assert.True(edition.IsUserSupplied);
    }
}
