using BookTracker.Application.Series;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the Series edit page's load (moved out of
// SeriesEditViewModel, #265): form fields, the books in order, and the
// comma-joined Author-role display.
[Trait("Category", TestCategories.Integration)]
public class GetSeriesDetailHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<SeriesDetail?> Load(int seriesId) =>
        new GetSeriesDetailHandler(_factory).HandleAsync(new GetSeriesDetail(seriesId));

    [Fact]
    public async Task MissingId_ReturnsNull()
    {
        Assert.Null(await Load(999999));
    }

    [Fact]
    public async Task LoadsFormFields_AndBooksByOrderThenTitle_UnorderedLast()
    {
        int seriesId;
        using (var db = _factory.CreateDbContext())
        {
            var series = new Series
            {
                Name = "Foundation", Author = "Isaac Asimov", Type = SeriesType.Series,
                ExpectedCount = 7, Description = "Psychohistory",
            };
            var asimov = Author.Create("Isaac Asimov");
            Book B(string title, int? order, string? display = null) => TestBooks.Book(
                title, series: series, seriesOrder: order, seriesOrderDisplay: display,
                works: [TestWorks.Work(title, asimov)]);
            db.Books.AddRange(
                B("Unnumbered B", null),
                B("Second Foundation", 3),
                B("Foundation", 1),
                B("Unnumbered A", null),
                B("Foundation and Empire", 2, "2"));
            db.Books.Add(TestBooks.Book("Not in series"));
            await db.SaveChangesAsync();
            seriesId = series.Id;
        }

        var detail = await Load(seriesId);

        Assert.NotNull(detail);
        Assert.Equal(seriesId, detail.Id);
        Assert.Equal("Foundation", detail.Name);
        Assert.Equal("Isaac Asimov", detail.Author);
        Assert.Equal(SeriesType.Series, detail.Type);
        Assert.Equal(7, detail.ExpectedCount);
        Assert.Equal("Psychohistory", detail.Description);
        Assert.Equal(
            ["Foundation", "Foundation and Empire", "Second Foundation", "Unnumbered A", "Unnumbered B"],
            detail.Books.Select(b => b.Title));
        var second = detail.Books[1];
        Assert.Equal(2, second.SeriesOrder);
        Assert.Equal("2", second.SeriesOrderDisplay);
        Assert.All(detail.Books, b => Assert.Equal("Isaac Asimov", b.Author));
    }

    [Fact]
    public async Task AuthorDisplay_IsDistinctAuthorRoleNames_LeadFirst_CommaJoined()
    {
        int seriesId;
        using (var db = _factory.CreateDbContext())
        {
            var series = new Series { Name = "Pendergast", Type = SeriesType.Series };
            var preston = Author.Create("Douglas Preston");
            var child = Author.Create("Lincoln Child");
            var editor = Author.Create("Ed Itor");
            db.Books.Add(TestBooks.Book(
                "Relic",
                series: series,
                seriesOrder: 1,
                works:
                [
                    TestWorks.Work(
                        "Relic",
                        authorships:
                        [
                            TestWorks.Authorship(child, 1),
                            TestWorks.Authorship(preston),
                            TestWorks.Authorship(editor, 0, AuthorRole.Editor),
                        ]),
                ]));
            await db.SaveChangesAsync();
            seriesId = series.Id;
        }

        var detail = await Load(seriesId);

        var row = Assert.Single(detail!.Books);
        Assert.Equal("Douglas Preston, Lincoln Child", row.Author); // editor left out
    }

    [Fact]
    public async Task SoftDeletedBooks_AreLeftOut()
    {
        int seriesId;
        using (var db = _factory.CreateDbContext())
        {
            var series = new Series { Name = "Discworld", Type = SeriesType.Series };
            db.Books.AddRange(
                TestBooks.Book("Mort", series: series, seriesOrder: 4),
                TestBooks.Book("Gone", series: series, seriesOrder: 5, deletedAt: DateTime.UtcNow));
            await db.SaveChangesAsync();
            seriesId = series.Id;
        }

        var detail = await Load(seriesId);

        Assert.Equal(["Mort"], detail!.Books.Select(b => b.Title));
    }
}
