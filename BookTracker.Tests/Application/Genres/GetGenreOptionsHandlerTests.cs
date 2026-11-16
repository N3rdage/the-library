using BookTracker.Application.Genres;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the genre picker's catalogue read (moved out of
// MudGenrePickerViewModel, #264): a flat list by name carrying the parent's
// id and name.
[Trait("Category", TestCategories.Integration)]
public class GetGenreOptionsHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task ReturnsFlatGenresByName_WithParentIdAndName()
    {
        int referenceId;
        using (var db = _factory.CreateDbContext())
        {
            var reference = new Genre { Name = "Reference" };
            db.Genres.AddRange(
                reference,
                new Genre { Name = "Dictionaries", ParentGenre = reference },
                new Genre { Name = "Atlases", ParentGenre = reference },
                new Genre { Name = "Fantasy" });
            await db.SaveChangesAsync();
            referenceId = reference.Id;
        }

        var options = await new GetGenreOptionsHandler(_factory).HandleAsync(new GetGenreOptions());

        Assert.Equal(["Atlases", "Dictionaries", "Fantasy", "Reference"], options.Select(g => g.Name));

        var dictionaries = options.Single(g => g.Name == "Dictionaries");
        Assert.Equal(referenceId, dictionaries.ParentGenreId);
        Assert.Equal("Reference", dictionaries.ParentName);

        var reference = options.Single(g => g.Name == "Reference");
        Assert.Equal(referenceId, reference.Id);
        Assert.Null(reference.ParentGenreId);
        Assert.Null(reference.ParentName);

        var fantasy = options.Single(g => g.Name == "Fantasy");
        Assert.Null(fantasy.ParentGenreId);
        Assert.Null(fantasy.ParentName);
    }
}
