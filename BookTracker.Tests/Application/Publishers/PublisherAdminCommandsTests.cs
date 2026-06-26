using BookTracker.Application.Publishers;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the /publishers admin write commands. Relocated from
// PublisherListViewModelTests when the writes moved into RenamePublisher /
// DeleteUnusedPublisher / MergePublishers (PR6b-2). The VM-side cache
// invalidation + toast wiring is covered by PublisherListViewModelTests.
[Trait("Category", TestCategories.Integration)]
public class PublisherAdminCommandsTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task RenamePublisher_TrimsAndUpdates_AndReportsChanged()
    {
        int publisherId;
        using (var db = _factory.CreateDbContext())
        {
            var p = new Publisher { Name = "Tor" };
            db.Publishers.Add(p);
            await db.SaveChangesAsync();
            publisherId = p.Id;
        }

        var result = await new RenamePublisherHandler(_factory).HandleAsync(new RenamePublisher(publisherId, "  Tor Books  "));

        Assert.True(result.Changed);
        using var db2 = _factory.CreateDbContext();
        Assert.Equal("Tor Books", db2.Publishers.Single(p => p.Id == publisherId).Name); // trimmed
    }

    [Fact]
    public async Task RenamePublisher_RefusesCollision_LeavesNameUnchanged()
    {
        int torBooksId;
        using (var db = _factory.CreateDbContext())
        {
            db.Publishers.Add(new Publisher { Name = "Tor" });
            var torBooks = new Publisher { Name = "Tor Books" };
            db.Publishers.Add(torBooks);
            await db.SaveChangesAsync();
            torBooksId = torBooks.Id;
        }

        var result = await new RenamePublisherHandler(_factory).HandleAsync(new RenamePublisher(torBooksId, "Tor"));

        Assert.False(result.Changed);
        Assert.Contains("already exists", result.Message);
        using var db2 = _factory.CreateDbContext();
        Assert.Equal("Tor Books", db2.Publishers.Single(p => p.Id == torBooksId).Name);
    }

    [Fact]
    public async Task DeleteUnusedPublisher_DeletesWhenZeroEditions()
    {
        int unusedId;
        using (var db = _factory.CreateDbContext())
        {
            var unused = new Publisher { Name = "Unused Press" };
            db.Publishers.Add(unused);
            await db.SaveChangesAsync();
            unusedId = unused.Id;
        }

        var result = await new DeleteUnusedPublisherHandler(_factory).HandleAsync(new DeleteUnusedPublisher(unusedId));

        Assert.True(result.Changed);
        using var db2 = _factory.CreateDbContext();
        Assert.Null(db2.Publishers.FirstOrDefault(p => p.Id == unusedId));
    }

    [Fact]
    public async Task DeleteUnusedPublisher_RefusesWhenEditionsReference()
    {
        int publisherId;
        using (var db = _factory.CreateDbContext())
        {
            var p = new Publisher { Name = "Tor" };
            db.Publishers.Add(p);
            db.Books.Add(new Book
            {
                Title = "Foo",
                Editions = [new Edition { Publisher = p, Format = BookFormat.Hardcover }]
            });
            await db.SaveChangesAsync();
            publisherId = p.Id;
        }

        var result = await new DeleteUnusedPublisherHandler(_factory).HandleAsync(new DeleteUnusedPublisher(publisherId));

        Assert.False(result.Changed);
        Assert.Contains("Can't delete", result.Message);
        using var db2 = _factory.CreateDbContext();
        Assert.NotNull(db2.Publishers.FirstOrDefault(p => p.Id == publisherId)); // still there
    }

    [Fact]
    public async Task MergePublishers_ReassignsEditionsAndDeletesSource()
    {
        int sourceId, targetId;
        using (var db = _factory.CreateDbContext())
        {
            var source = new Publisher { Name = "Tor Books" };
            var target = new Publisher { Name = "Tor" };
            db.Publishers.AddRange(source, target);
            db.Books.Add(new Book
            {
                Title = "Foo",
                Editions =
                [
                    new Edition { Publisher = source, Format = BookFormat.Hardcover },
                    new Edition { Publisher = source, Format = BookFormat.TradePaperback }
                ]
            });
            await db.SaveChangesAsync();
            sourceId = source.Id;
            targetId = target.Id;
        }

        var result = await new MergePublishersHandler(_factory).HandleAsync(new MergePublishers(sourceId, targetId));

        Assert.True(result.Changed);
        Assert.Contains("reassigned", result.Message);
        using var db2 = _factory.CreateDbContext();
        Assert.Null(db2.Publishers.FirstOrDefault(p => p.Id == sourceId));
        Assert.Equal(2, db2.Editions.Count(e => e.PublisherId == targetId));
    }

    [Fact]
    public async Task MergePublishers_NoOpWhenSourceEqualsTarget()
    {
        int publisherId;
        using (var db = _factory.CreateDbContext())
        {
            var p = new Publisher { Name = "Tor" };
            db.Publishers.Add(p);
            db.Books.Add(new Book
            {
                Title = "Foo",
                Editions = [new Edition { Publisher = p, Format = BookFormat.Hardcover }]
            });
            await db.SaveChangesAsync();
            publisherId = p.Id;
        }

        var result = await new MergePublishersHandler(_factory).HandleAsync(new MergePublishers(publisherId, publisherId));

        Assert.False(result.Changed);
        using var db2 = _factory.CreateDbContext();
        Assert.NotNull(db2.Publishers.FirstOrDefault(p => p.Id == publisherId));
        Assert.Equal(1, db2.Editions.Count(e => e.PublisherId == publisherId));
    }
}
