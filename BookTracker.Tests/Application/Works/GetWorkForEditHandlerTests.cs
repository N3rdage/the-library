using BookTracker.Application.Works;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the Edit work dialog's prefill read (moved out of
// WorkEditDialogViewModel, #264).
[Trait("Category", TestCategories.Integration)]
public class GetWorkForEditHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<WorkForEdit?> Load(int workId) =>
        new GetWorkForEditHandler(_factory).HandleAsync(new GetWorkForEdit(workId));

    [Fact]
    public async Task MissingId_ReturnsNull()
    {
        Assert.Null(await Load(999));
    }

    [Fact]
    public async Task LoadsScalarFields()
    {
        int workId;
        using (var db = _factory.CreateDbContext())
        {
            var work = new Work
            {
                Title = "Mort",
                Subtitle = "A Discworld Novel",
                WorkAuthors = [new WorkAuthor { Author = Author.Create("Terry Pratchett"), Order = 0 }],
                FirstPublishedDate = new DateOnly(1987, 11, 12),
                FirstPublishedDatePrecision = DatePrecision.Day,
            };
            db.Books.Add(new Book { Title = "Mort", Works = [work] });
            await db.SaveChangesAsync();
            workId = work.Id;
        }

        var form = await Load(workId);

        Assert.NotNull(form);
        Assert.Equal(workId, form.Id);
        Assert.Equal("Mort", form.Title);
        Assert.Equal("A Discworld Novel", form.Subtitle);
        Assert.Equal(["Terry Pratchett"], form.AuthorNames);
        Assert.Empty(form.Contributors);
        Assert.Equal(new DateOnly(1987, 11, 12), form.FirstPublishedDate);
        Assert.Equal(DatePrecision.Day, form.FirstPublishedDatePrecision);
        Assert.Empty(form.GenreIds);
    }

    [Fact]
    public async Task SplitsAuthorsFromContributors_InChipOrder()
    {
        // Authors by Order (lead first); contributors by role, then order.
        int workId;
        using (var db = _factory.CreateDbContext())
        {
            var work = new Work
            {
                Title = "Good Omens",
                WorkAuthors =
                [
                    new WorkAuthor { Author = Author.Create("Neil Gaiman"), Order = 1 },
                    new WorkAuthor { Author = Author.Create("Terry Pratchett"), Order = 0 },
                    new WorkAuthor { Author = Author.Create("Ann Translator"), Role = AuthorRole.Translator, Order = 0 },
                    new WorkAuthor { Author = Author.Create("Zed Editor"), Role = AuthorRole.Editor, Order = 1 },
                    new WorkAuthor { Author = Author.Create("Amy Editor"), Role = AuthorRole.Editor, Order = 0 },
                ],
            };
            db.Books.Add(new Book { Title = "Good Omens", Works = [work] });
            await db.SaveChangesAsync();
            workId = work.Id;
        }

        var form = await Load(workId);

        Assert.NotNull(form);
        Assert.Equal(["Terry Pratchett", "Neil Gaiman"], form.AuthorNames);
        Assert.Equal(
            [
                new WorkContributorView("Amy Editor", AuthorRole.Editor),
                new WorkContributorView("Zed Editor", AuthorRole.Editor),
                new WorkContributorView("Ann Translator", AuthorRole.Translator),
            ],
            form.Contributors);
    }

    [Fact]
    public async Task LoadsSelectedGenreIds()
    {
        int workId, fantasyId, horrorId;
        using (var db = _factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "Fantasy" };
            var horror = new Genre { Name = "Horror" };
            db.Genres.AddRange(fantasy, horror, new Genre { Name = "Unused" });
            var work = new Work
            {
                Title = "w",
                WorkAuthors = [new WorkAuthor { Author = Author.Create("a"), Order = 0 }],
                Genres = [fantasy, horror],
            };
            db.Books.Add(new Book { Title = "B", Works = [work] });
            await db.SaveChangesAsync();
            workId = work.Id;
            fantasyId = fantasy.Id;
            horrorId = horror.Id;
        }

        var form = await Load(workId);

        Assert.NotNull(form);
        Assert.Equal([fantasyId, horrorId], form.GenreIds.Order());
    }
}
