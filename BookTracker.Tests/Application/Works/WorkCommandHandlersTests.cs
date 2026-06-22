using BookTracker.Application;
using BookTracker.Application.Works;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the Works command handlers against the SQL container.
[Trait("Category", TestCategories.Integration)]
public class WorkCommandHandlersTests
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<(int bookId, int workId)> SeedBookWithWorkAsync(string title = "Mort")
    {
        await using var db = _factory.CreateDbContext();
        var work = new Work
        {
            Title = title,
            WorkAuthors = { new WorkAuthor { Author = new Author { Name = "Pratchett" }, Order = 0, Role = AuthorRole.Author } },
        };
        var book = new Book { Title = "Book", Works = { work } };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return (book.Id, work.Id);
    }

    private async Task<int> SeedBareBookAsync(string title = "Bare")
    {
        await using var db = _factory.CreateDbContext();
        var book = new Book { Title = title };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    [Fact]
    public async Task AttachWorkToBook_attachesExistingWork_toASecondBook()
    {
        var (_, workId) = await SeedBookWithWorkAsync("Shared Story");
        var targetBook = await SeedBareBookAsync("Compendium");

        var title = await new AttachWorkToBookHandler(_factory).HandleAsync(new AttachWorkToBook(targetBook, workId));

        Assert.Equal("Shared Story", title);
        await using var db = _factory.CreateDbContext();
        var work = await db.Works.Include(w => w.Books).FirstAsync(w => w.Id == workId);
        Assert.Equal(2, work.Books.Count); // now on both books
    }

    [Fact]
    public async Task AttachWorkToBook_alreadyOnBook_returnsNull()
    {
        var (bookId, workId) = await SeedBookWithWorkAsync();
        var result = await new AttachWorkToBookHandler(_factory).HandleAsync(new AttachWorkToBook(bookId, workId));
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveWorkFromBook_exclusiveWork_deletesIt()
    {
        var (bookId, workId) = await SeedBookWithWorkAsync();

        var title = await new RemoveWorkFromBookHandler(_factory).HandleAsync(new RemoveWorkFromBook(bookId, workId));

        Assert.Equal("Mort", title);
        await using var db = _factory.CreateDbContext();
        Assert.Null(await db.Works.FindAsync(workId)); // orphaned → deleted
    }

    [Fact]
    public async Task RemoveWorkFromBook_sharedWork_keepsItOnTheOtherBook()
    {
        int bookA, workId;
        await using (var db = _factory.CreateDbContext())
        {
            var work = new Work
            {
                Title = "Shared",
                WorkAuthors = { new WorkAuthor { Author = new Author { Name = "A" }, Order = 0, Role = AuthorRole.Author } },
            };
            var a = new Book { Title = "A", Works = { work } };
            var b = new Book { Title = "B", Works = { work } };
            db.Books.AddRange(a, b);
            await db.SaveChangesAsync();
            bookA = a.Id; workId = work.Id;
        }

        await new RemoveWorkFromBookHandler(_factory).HandleAsync(new RemoveWorkFromBook(bookA, workId));

        await using var verify = _factory.CreateDbContext();
        var survivor = await verify.Works.Include(w => w.Books).FirstOrDefaultAsync(w => w.Id == workId);
        Assert.NotNull(survivor);              // not orphaned — survives
        Assert.Single(survivor!.Books);
    }

    [Fact]
    public async Task CreateWorkOnBook_createsWorkAttachedWithAuthorAndGenre()
    {
        var bookId = await SeedBareBookAsync();
        int genreId;
        await using (var db = _factory.CreateDbContext())
        {
            var g = new Genre { Name = "Fantasy" };
            db.Genres.Add(g);
            await db.SaveChangesAsync();
            genreId = g.Id;
        }

        var workId = await new CreateWorkOnBookHandler(_factory).HandleAsync(new CreateWorkOnBook(
            bookId, "New Story", "A Subtitle", ["Brand New Author"], [],
            new DateOnly(2001, 1, 1), DatePrecision.Year, [genreId]));

        Assert.NotNull(workId);
        await using var verify = _factory.CreateDbContext();
        var work = await verify.Works
            .Include(w => w.Books)
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(w => w.Genres)
            .FirstAsync(w => w.Id == workId);
        Assert.Equal("New Story", work.Title);
        Assert.Contains(work.Books, b => b.Id == bookId);          // born attached
        Assert.Equal("Brand New Author", work.WorkAuthors.Single().Author.Name);
        Assert.Contains(work.Genres, g => g.Id == genreId);
    }

    [Fact]
    public async Task CreateWorkOnBook_noContributors_returnsNull_andCreatesNothing()
    {
        var bookId = await SeedBareBookAsync();

        var workId = await new CreateWorkOnBookHandler(_factory).HandleAsync(new CreateWorkOnBook(
            bookId, "Orphan", null, [], [], null, DatePrecision.Day, []));

        Assert.Null(workId);
        await using var db = _factory.CreateDbContext();
        Assert.Empty(db.Works);
    }

    [Fact]
    public async Task UpdateWork_persistsTitleAuthorAndSeries()
    {
        var (_, workId) = await SeedBookWithWorkAsync();
        int seriesId;
        await using (var db = _factory.CreateDbContext())
        {
            var s = new Series { Name = "Discworld", Type = SeriesType.Series };
            db.Series.Add(s);
            await db.SaveChangesAsync();
            seriesId = s.Id;
        }

        await new UpdateWorkHandler(_factory).HandleAsync(new UpdateWork(
            workId, "Mort (revised)", null, ["Terry Pratchett"], [],
            null, DatePrecision.Day, [], seriesId, 4, null));

        await using var verify = _factory.CreateDbContext();
        var work = await verify.Works.Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author).FirstAsync(w => w.Id == workId);
        Assert.Equal("Mort (revised)", work.Title);
        Assert.Equal("Terry Pratchett", work.WorkAuthors.Single().Author.Name);
        Assert.Equal(seriesId, work.SeriesId);
        Assert.Equal(4, work.SeriesOrder);
    }

    [Fact]
    public async Task UpdateWork_missing_throwsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new UpdateWorkHandler(_factory).HandleAsync(new UpdateWork(
                424242, "x", null, ["A"], [], null, DatePrecision.Day, [], null, null, null)));
    }

    [Fact]
    public async Task AttachWorksToBook_mixOfNewAndExisting_attachesAll()
    {
        var (_, existingWorkId) = await SeedBookWithWorkAsync("Apt Pupil");
        var targetBook = await SeedBareBookAsync("Four Past Midnight");

        var rows = new List<WorkRow>
        {
            new(null, "The Library Policeman", null, null, DatePrecision.Day, ["Stephen King"], [], []),
            new(existingWorkId, "Apt Pupil", null, null, DatePrecision.Day, [], [], []),
        };

        var count = await new AttachWorksToBookHandler(_factory).HandleAsync(
            new AttachWorksToBook(targetBook, rows, SingleAuthor: false, SingleGenre: false, SharedAuthors: [], SharedGenreIds: []));

        Assert.Equal(2, count);
        await using var db = _factory.CreateDbContext();
        var book = await db.Books.Include(b => b.Works).FirstAsync(b => b.Id == targetBook);
        Assert.Equal(2, book.Works.Count);
        Assert.Contains(book.Works, w => w.Id == existingWorkId);
        Assert.Contains(book.Works, w => w.Title == "The Library Policeman");
    }

    [Fact]
    public async Task AttachWorksToBook_allBlankRows_throwsUserFacing()
    {
        var bookId = await SeedBareBookAsync();
        var rows = new List<WorkRow> { new(null, "   ", null, null, DatePrecision.Day, [], [], []) };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AttachWorksToBookHandler(_factory).HandleAsync(
                new AttachWorksToBook(bookId, rows, false, false, [], [])));
        Assert.Contains("at least one work", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
