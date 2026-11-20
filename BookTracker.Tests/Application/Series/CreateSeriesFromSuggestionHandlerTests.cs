using BookTracker.Application.Series;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for the AI assistant's "create series from a suggested
// grouping" command. Each test seeds in one context: Author.Name is unique, so
// an author is created once and shared by every Work that names it.
[Trait("Category", TestCategories.Integration)]
public class CreateSeriesFromSuggestionHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private static Book NewBook(string title, Author author) => TestBooks.Book(
        title,
        works: [TestWorks.Work(title, author)]);

    [Fact]
    public async Task CreateSeriesFromSuggestion_createsSeries_assignsUnseriesedMatches_inOrder()
    {
        int seriesedBookId;
        await using (var db = _factory.CreateDbContext())
        {
            var other = TestSeries.Series("AIA Other", SeriesType.Series);
            db.Series.Add(other);
            await db.SaveChangesAsync();
            var b = TestBooks.Book("Already Seriesed");
            b.AssignToSeries(other.Id, 4, null);
            db.Books.Add(b);

            var asimov = Author.Create("Isaac Asimov");
            db.Books.AddRange(
                NewBook("Foundation", asimov),
                NewBook("Foundation and Empire", asimov));
            await db.SaveChangesAsync();
            seriesedBookId = b.Id;
        }

        var id = await new CreateSeriesFromSuggestionHandler(_factory).HandleAsync(new CreateSeriesFromSuggestion(
            "AIA Foundation", SeriesType.Series, "Asimov's saga",
            ["Foundation", "Foundation and Empire", "Already Seriesed", "Missing"]));

        await using var check = _factory.CreateDbContext();
        var series = await check.Series.Include(s => s.Books).SingleAsync(s => s.Name == "AIA Foundation");
        Assert.Equal(series.Id, id);
        Assert.Equal(SeriesType.Series, series.Type);
        Assert.Equal("Asimov's saga", series.Description);
        Assert.Equal("Isaac Asimov", series.Author);
        Assert.Equal(new[] { 1, 2 }, series.Books.Select(b => b.SeriesOrder!.Value).OrderBy(o => o));
        var untouched = await check.Books.SingleAsync(b => b.Id == seriesedBookId);
        Assert.Equal(4, untouched.SeriesOrder);
        Assert.NotEqual(series.Id, untouched.SeriesId);
    }

    [Fact]
    public async Task CreateSeriesFromSuggestion_existingName_isNoOp()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Series.Add(TestSeries.Series("AIA Dup", SeriesType.Collection));
            db.Books.Add(NewBook("Lonely", Author.Create("Someone")));
            await db.SaveChangesAsync();
        }

        var id = await new CreateSeriesFromSuggestionHandler(_factory).HandleAsync(
            new CreateSeriesFromSuggestion("AIA Dup", SeriesType.Series, "r", ["Lonely"]));

        Assert.Null(id);
        await using var check = _factory.CreateDbContext();
        Assert.Equal(1, await check.Series.CountAsync(s => s.Name == "AIA Dup"));
        Assert.Null((await check.Books.SingleAsync(b => b.Title == "Lonely")).SeriesId);
    }

    [Fact]
    public async Task CreateSeriesFromSuggestion_existingNameWithPadding_isNoOp_notADbUpdateException()
    {
        // The aggregate stores the trimmed name, so the pre-check compares the
        // trimmed name too: a padded duplicate is the same no-op, not a raw
        // unique-index violation on insert.
        await using (var db = _factory.CreateDbContext())
        {
            db.Series.Add(TestSeries.Series("AIA Dup", SeriesType.Collection));
            db.Books.Add(NewBook("Lonely", Author.Create("Someone")));
            await db.SaveChangesAsync();
        }

        var id = await new CreateSeriesFromSuggestionHandler(_factory).HandleAsync(
            new CreateSeriesFromSuggestion("  AIA Dup  ", SeriesType.Series, "r", ["Lonely"]));

        Assert.Null(id);
        await using var check = _factory.CreateDbContext();
        Assert.Equal(1, await check.Series.CountAsync());
        Assert.Null((await check.Books.SingleAsync(b => b.Title == "Lonely")).SeriesId);
    }

    [Fact]
    public async Task CreateSeriesFromSuggestion_blankName_throwsDomainRule_persistsNothing()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Books.Add(NewBook("Lonely", Author.Create("Someone")));
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new CreateSeriesFromSuggestionHandler(_factory).HandleAsync(
                new CreateSeriesFromSuggestion("   ", SeriesType.Series, "r", ["Lonely"])));

        await using var check = _factory.CreateDbContext();
        Assert.Empty(check.Series);
        Assert.Null((await check.Books.SingleAsync(b => b.Title == "Lonely")).SeriesId);
    }

    [Fact]
    public async Task CreateSeriesFromSuggestion_mixedAuthors_leaveAuthorNull()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Books.AddRange(
                NewBook("Anthology A", Author.Create("Writer One")),
                NewBook("Anthology B", Author.Create("Writer Two")));
            await db.SaveChangesAsync();
        }

        await new CreateSeriesFromSuggestionHandler(_factory).HandleAsync(
            new CreateSeriesFromSuggestion("AIA Mixed", SeriesType.Collection, "r", ["Anthology A", "Anthology B"]));

        await using var check = _factory.CreateDbContext();
        var series = await check.Series.Include(s => s.Books).SingleAsync(s => s.Name == "AIA Mixed");
        Assert.Equal(SeriesType.Collection, series.Type);
        Assert.Null(series.Author);
        Assert.Equal(2, series.Books.Count);
    }
}
