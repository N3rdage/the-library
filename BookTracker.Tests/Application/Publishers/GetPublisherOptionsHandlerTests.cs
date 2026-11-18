using BookTracker.Application.Publishers;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the edition forms' publisher cache read (moved out of
// EditionFormDialogViewModel and EditionFormViewModel, #264).
[Trait("Category", TestCategories.Integration)]
public class GetPublisherOptionsHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task ReturnsEveryPublisherOrderedByName()
    {
        int corgiId;
        using (var db = _factory.CreateDbContext())
        {
            var corgi = new Publisher { Name = "Corgi" };
            db.Publishers.AddRange(new Publisher { Name = "Orbit" }, corgi, new Publisher { Name = "Gollancz" });
            await db.SaveChangesAsync();
            corgiId = corgi.Id;
        }

        var options = await new GetPublisherOptionsHandler(_factory).HandleAsync(new GetPublisherOptions());

        Assert.Equal(["Corgi", "Gollancz", "Orbit"], options.Select(p => p.Name));
        Assert.Equal(corgiId, options[0].Id);
    }

    [Fact]
    public async Task NoPublishers_ReturnsEmpty()
    {
        var options = await new GetPublisherOptionsHandler(_factory).HandleAsync(new GetPublisherOptions());

        Assert.Empty(options);
    }
}
