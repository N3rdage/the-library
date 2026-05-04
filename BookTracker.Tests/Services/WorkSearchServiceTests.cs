using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

public class WorkSearchServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private WorkSearchService CreateService() => new(_factory);

    [Fact]
    public async Task SearchAsync_returns_empty_when_query_too_short()
    {
        await SeedWorksAsync(("The Hobbit", "Tolkien"), ("Hamlet", "Shakespeare"));

        var result = await CreateService().SearchAsync("h");

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_matches_substring_case_insensitive()
    {
        await SeedWorksAsync(
            ("The Hobbit", "Tolkien"),
            ("A Hobbit's Tale", "Other"),
            ("Macbeth", "Shakespeare"));

        var result = await CreateService().SearchAsync("hobbit");

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Contains("Hobbit", r.Title, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_prefers_starts_with_matches()
    {
        await SeedWorksAsync(
            ("The Hobbit", "A"),
            ("Hobbit Encyclopaedia", "B"));

        var result = await CreateService().SearchAsync("hobbit");

        // "Hobbit Encyclopaedia" starts with "hobbit" → should come first.
        Assert.Equal("Hobbit Encyclopaedia", result[0].Title);
        Assert.Equal("The Hobbit", result[1].Title);
    }

    [Fact]
    public async Task SearchAsync_excludes_works_already_attached_to_given_book()
    {
        using var db = _factory.CreateDbContext();
        var author = new Author { Name = "Tolkien" };
        var hobbit = new Work { Title = "The Hobbit", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        var fellowship = new Work { Title = "Fellowship", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] };
        db.Books.Add(new Book { Title = "Compendium", Works = [hobbit] });
        db.Books.Add(new Book { Title = "Other", Works = [fellowship] });
        await db.SaveChangesAsync();

        var compendiumId = db.Books.Single(b => b.Title == "Compendium").Id;

        var result = await CreateService().SearchAsync("the hobbit", excludeBookId: compendiumId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_honours_maxResults()
    {
        await SeedWorksAsync(
            ("Hobbit 1", "A"),
            ("Hobbit 2", "A"),
            ("Hobbit 3", "A"),
            ("Hobbit 4", "A"));

        var result = await CreateService().SearchAsync("hobbit", maxResults: 2);

        Assert.Equal(2, result.Count);
    }

    private async Task SeedWorksAsync(params (string Title, string AuthorName)[] data)
    {
        using var db = _factory.CreateDbContext();
        foreach (var (title, authorName) in data)
        {
            var author = db.Authors.FirstOrDefault(a => a.Name == authorName)
                         ?? new Author { Name = authorName };
            db.Books.Add(new Book
            {
                Title = title,
                Works = [new Work { Title = title, WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }]
            });
        }
        await db.SaveChangesAsync();
    }
}
