using BookTracker.Application;
using BookTracker.Application.Series;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the Series command handlers against the SQL container.
[Trait("Category", TestCategories.Integration)]
public class SeriesCommandHandlersTests
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<int> SeedSeriesAsync(string name = "Discworld", SeriesType type = SeriesType.Series)
    {
        await using var db = _factory.CreateDbContext();
        var s = new Series { Name = name, Type = type };
        db.Series.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    private async Task<int> SeedWorkAsync(string title, int? seriesId = null, int? order = null, string? orderDisplay = null)
    {
        await using var db = _factory.CreateDbContext();
        var work = new Work
        {
            Title = title,
            WorkAuthors = { new WorkAuthor { Author = new Author { Name = $"Author of {title}" }, Order = 0, Role = AuthorRole.Author } },
            SeriesId = seriesId,
            SeriesOrder = order,
            SeriesOrderDisplay = orderDisplay,
        };
        db.Books.Add(new Book { Title = title, Works = { work } });
        await db.SaveChangesAsync();
        return work.Id;
    }

    // Seeds a Book carrying series membership directly (the post-cutover home).
    private async Task<int> SeedBookAsync(string title, int? seriesId = null, int? order = null, string? orderDisplay = null)
    {
        await using var db = _factory.CreateDbContext();
        var work = new Work
        {
            Title = title,
            WorkAuthors = { new WorkAuthor { Author = new Author { Name = $"Author of {title}" }, Order = 0, Role = AuthorRole.Author } },
        };
        var book = new Book
        {
            Title = title,
            Works = { work },
            SeriesId = seriesId,
            SeriesOrder = order,
            SeriesOrderDisplay = orderDisplay,
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    // --- CreateSeries --------------------------------------------------------

    [Fact]
    public async Task CreateSeries_persistsFields_andReturnsId()
    {
        var id = await new CreateSeriesHandler(_factory).HandleAsync(
            new CreateSeries("Foundation", "Isaac Asimov", SeriesType.Series, 7, "Psychohistory."));

        await using var db = _factory.CreateDbContext();
        var saved = await db.Series.FindAsync(id);
        Assert.NotNull(saved);
        Assert.Equal("Foundation", saved!.Name);
        Assert.Equal("Isaac Asimov", saved.Author);
        Assert.Equal(7, saved.ExpectedCount);
        Assert.Equal("Psychohistory.", saved.Description);
    }

    [Fact]
    public async Task CreateSeries_collection_dropsExpectedCount()
    {
        var id = await new CreateSeriesHandler(_factory).HandleAsync(
            new CreateSeries("Hercule Poirot", null, SeriesType.Collection, 33, null));

        await using var db = _factory.CreateDbContext();
        var saved = await db.Series.FindAsync(id);
        Assert.Null(saved!.ExpectedCount);
    }

    [Fact]
    public async Task CreateSeries_duplicateName_throwsDomainRule()
    {
        await SeedSeriesAsync("Discworld");

        await Assert.ThrowsAsync<BookTracker.Data.DomainRuleException>(() =>
            new CreateSeriesHandler(_factory).HandleAsync(
                new CreateSeries("discworld", null, SeriesType.Series, null, null))); // case-insensitive clash
    }

    [Fact]
    public async Task CreateSeries_blankName_throwsDomainRule()
    {
        await Assert.ThrowsAsync<BookTracker.Data.DomainRuleException>(() =>
            new CreateSeriesHandler(_factory).HandleAsync(
                new CreateSeries("   ", null, SeriesType.Series, null, null)));
    }

    // --- UpdateSeries --------------------------------------------------------

    [Fact]
    public async Task UpdateSeries_updatesFields()
    {
        var id = await SeedSeriesAsync("Old Name");

        await new UpdateSeriesHandler(_factory).HandleAsync(
            new UpdateSeries(id, "New Name", "New Author", SeriesType.Series, 9, "desc"));

        await using var db = _factory.CreateDbContext();
        var saved = await db.Series.FindAsync(id);
        Assert.Equal("New Name", saved!.Name);
        Assert.Equal("New Author", saved.Author);
        Assert.Equal(9, saved.ExpectedCount);
    }

    [Fact]
    public async Task UpdateSeries_renameToOwnName_isAllowed()
    {
        var id = await SeedSeriesAsync("Keep");

        // Same name (this row) + a new author — the uniqueness guard excludes self.
        await new UpdateSeriesHandler(_factory).HandleAsync(
            new UpdateSeries(id, "Keep", "Added Author", SeriesType.Series, null, null));

        await using var db = _factory.CreateDbContext();
        Assert.Equal("Added Author", (await db.Series.FindAsync(id))!.Author);
    }

    [Fact]
    public async Task UpdateSeries_renameOntoAnother_throwsDomainRule()
    {
        await SeedSeriesAsync("Alpha");
        var bId = await SeedSeriesAsync("Beta");

        await Assert.ThrowsAsync<BookTracker.Data.DomainRuleException>(() =>
            new UpdateSeriesHandler(_factory).HandleAsync(
                new UpdateSeries(bId, "Alpha", null, SeriesType.Series, null, null)));
    }

    [Fact]
    public async Task UpdateSeries_missing_throwsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new UpdateSeriesHandler(_factory).HandleAsync(
                new UpdateSeries(424242, "x", null, SeriesType.Series, null, null)));
    }

    // --- DeleteSeries --------------------------------------------------------

    [Fact]
    public async Task DeleteSeries_removesSeries_andSetNullsMemberWorks()
    {
        var seriesId = await SeedSeriesAsync();
        var workId = await SeedWorkAsync("Mort", seriesId, order: 4);

        await new DeleteSeriesHandler(_factory).HandleAsync(new DeleteSeries(seriesId));

        await using var db = _factory.CreateDbContext();
        Assert.Null(await db.Series.FindAsync(seriesId));   // gone
        var work = await db.Works.FindAsync(workId);
        Assert.NotNull(work);                                // work survives
        Assert.Null(work!.SeriesId);                         // link cleared by FK SetNull
    }

    [Fact]
    public async Task DeleteSeries_missing_isNoOp()
    {
        // No throw — idempotent, matching the old ViewModel.
        await new DeleteSeriesHandler(_factory).HandleAsync(new DeleteSeries(999999));
    }

    // --- AddBookToSeries -----------------------------------------------------

    [Fact]
    public async Task AddBookToSeries_firstBook_getsOrderOne()
    {
        var seriesId = await SeedSeriesAsync();
        var bookId = await SeedBookAsync("Guards! Guards!");

        await new AddBookToSeriesHandler(_factory).HandleAsync(new AddBookToSeries(seriesId, bookId));

        await using var db = _factory.CreateDbContext();
        var book = await db.Books.FindAsync(bookId);
        Assert.Equal(seriesId, book!.SeriesId);
        Assert.Equal(1, book.SeriesOrder);
    }

    [Fact]
    public async Task AddBookToSeries_appendsAfterHighestOrder()
    {
        var seriesId = await SeedSeriesAsync();
        await SeedBookAsync("Colour of Magic", seriesId, order: 1);
        await SeedBookAsync("The Light Fantastic", seriesId, order: 2);
        var newBookId = await SeedBookAsync("Equal Rites");

        await new AddBookToSeriesHandler(_factory).HandleAsync(new AddBookToSeries(seriesId, newBookId));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(3, (await db.Books.FindAsync(newBookId))!.SeriesOrder);
    }

    [Fact]
    public async Task AddBookToSeries_missingBook_isNoOp()
    {
        var seriesId = await SeedSeriesAsync();
        await new AddBookToSeriesHandler(_factory).HandleAsync(new AddBookToSeries(seriesId, 999999));
    }

    // --- RemoveBookFromSeries ------------------------------------------------

    [Fact]
    public async Task RemoveBookFromSeries_clearsLinkOrderAndDisplay()
    {
        var seriesId = await SeedSeriesAsync();
        var bookId = await SeedBookAsync("Edgedancer", seriesId, order: 4, orderDisplay: "4.5");

        await new RemoveBookFromSeriesHandler(_factory).HandleAsync(new RemoveBookFromSeries(bookId));

        await using var db = _factory.CreateDbContext();
        var book = await db.Books.FindAsync(bookId);
        Assert.Null(book!.SeriesId);
        Assert.Null(book.SeriesOrder);
        Assert.Null(book.SeriesOrderDisplay);   // the consistency fix — no dangling "4.5"
    }

    [Fact]
    public async Task RemoveBookFromSeries_missingBook_isNoOp()
    {
        await new RemoveBookFromSeriesHandler(_factory).HandleAsync(new RemoveBookFromSeries(999999));
    }

    // --- SetBookSeriesOrder --------------------------------------------------

    [Fact]
    public async Task SetBookSeriesOrder_updatesOrder_keepsMembership()
    {
        var seriesId = await SeedSeriesAsync();
        var bookId = await SeedBookAsync("Words of Radiance", seriesId, order: 2);

        await new SetBookSeriesOrderHandler(_factory).HandleAsync(
            new SetBookSeriesOrder(bookId, 4, "4.5"));

        await using var db = _factory.CreateDbContext();
        var book = await db.Books.FindAsync(bookId);
        Assert.Equal(seriesId, book!.SeriesId);   // still in the series
        Assert.Equal(4, book.SeriesOrder);
        Assert.Equal("4.5", book.SeriesOrderDisplay);
    }
}
