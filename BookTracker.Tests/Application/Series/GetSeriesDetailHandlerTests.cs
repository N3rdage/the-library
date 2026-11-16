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
            var asimov = new Author { Name = "Isaac Asimov" };
            Book B(string title, int? order, string? display = null) => new()
            {
                Title = title, Series = series, SeriesOrder = order, SeriesOrderDisplay = display,
                Works = [new Work { Title = title, WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }],
            };
            db.Books.AddRange(
                B("Unnumbered B", null),
                B("Second Foundation", 3),
                B("Foundation", 1),
                B("Unnumbered A", null),
                B("Foundation and Empire", 2, "2"));
            db.Books.Add(new Book { Title = "Not in series" });
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
            var preston = new Author { Name = "Douglas Preston" };
            var child = new Author { Name = "Lincoln Child" };
            var editor = new Author { Name = "Ed Itor" };
            db.Books.Add(new Book
            {
                Title = "Relic",
                Series = series,
                SeriesOrder = 1,
                Works =
                [
                    new Work
                    {
                        Title = "Relic",
                        WorkAuthors =
                        [
                            new WorkAuthor { Author = child, Order = 1, Role = AuthorRole.Author },
                            new WorkAuthor { Author = preston, Order = 0, Role = AuthorRole.Author },
                            new WorkAuthor { Author = editor, Order = 0, Role = AuthorRole.Editor },
                        ],
                    },
                ],
            });
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
                new Book { Title = "Mort", Series = series, SeriesOrder = 4 },
                new Book { Title = "Gone", Series = series, SeriesOrder = 5, DeletedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            seriesId = series.Id;
        }

        var detail = await Load(seriesId);

        Assert.Equal(["Mort"], detail!.Books.Select(b => b.Title));
    }
}
