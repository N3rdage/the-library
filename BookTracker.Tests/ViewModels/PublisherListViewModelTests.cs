using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Integration)]
public class PublisherListViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesPublisherRows_WithEditionCounts()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var tor = new Publisher { Name = "Tor" };
            var unused = new Publisher { Name = "Unused Press" };
            db.Publishers.AddRange(tor, unused);
            db.Books.Add(new Book
            {
                Title = "Foo",
                Editions =
                [
                    new Edition { Publisher = tor, Format = BookFormat.Hardcover },
                    new Edition { Publisher = tor, Format = BookFormat.TradePaperback }
                ]
            });
            await db.SaveChangesAsync();
        }

        var vm = new PublisherListViewModel(factory);
        await vm.LoadAsync();

        Assert.Equal(2, vm.Publishers.Count);
        var torRow = vm.Publishers.Single(p => p.Name == "Tor");
        Assert.Equal(2, torRow.EditionCount);
        Assert.Equal(0, vm.Publishers.Single(p => p.Name == "Unused Press").EditionCount);
    }

    [Fact]
    public async Task RenameAsync_UpdatesName_AndInvalidatesDetailCache()
    {
        var factory = new TestDbContextFactory();
        int publisherId;
        using (var db = factory.CreateDbContext())
        {
            var p = new Publisher { Name = "Tor" };
            db.Publishers.Add(p);
            await db.SaveChangesAsync();
            publisherId = p.Id;
        }

        var vm = new PublisherListViewModel(factory);
        await vm.LoadAsync();
        await vm.ToggleExpandAsync(publisherId);
        Assert.True(vm.DetailByPublisherId.ContainsKey(publisherId));

        await vm.RenameAsync(publisherId, "  Tor Books  ");

        Assert.Equal("Tor Books", vm.Publishers.Single().Name); // trimmed
        Assert.False(vm.DetailByPublisherId.ContainsKey(publisherId));
    }

    [Fact]
    public async Task RenameAsync_RefusesCollisionWithAnotherPublisher()
    {
        var factory = new TestDbContextFactory();
        int torBooksId;
        using (var db = factory.CreateDbContext())
        {
            db.Publishers.Add(new Publisher { Name = "Tor" });
            var torBooks = new Publisher { Name = "Tor Books" };
            db.Publishers.Add(torBooks);
            await db.SaveChangesAsync();
            torBooksId = torBooks.Id;
        }

        var vm = new PublisherListViewModel(factory);
        await vm.LoadAsync();
        await vm.RenameAsync(torBooksId, "Tor");

        Assert.Contains("already exists", vm.SuccessMessage);
        Assert.Equal("Tor Books", vm.Publishers.Single(p => p.Id == torBooksId).Name);
    }

    [Fact]
    public async Task DeleteUnusedAsync_DeletesPublisherWithZeroEditions()
    {
        var factory = new TestDbContextFactory();
        int unusedId;
        using (var db = factory.CreateDbContext())
        {
            var unused = new Publisher { Name = "Unused Press" };
            db.Publishers.Add(unused);
            await db.SaveChangesAsync();
            unusedId = unused.Id;
        }

        var vm = new PublisherListViewModel(factory);
        await vm.LoadAsync();
        await vm.DeleteUnusedAsync(unusedId);

        Assert.Empty(vm.Publishers);
    }

    [Fact]
    public async Task DeleteUnusedAsync_RefusesWhenEditionsStillReference()
    {
        var factory = new TestDbContextFactory();
        int publisherId;
        using (var db = factory.CreateDbContext())
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

        var vm = new PublisherListViewModel(factory);
        await vm.LoadAsync();
        await vm.DeleteUnusedAsync(publisherId);

        Assert.Contains("Can't delete", vm.SuccessMessage);
        Assert.Single(vm.Publishers); // still there
    }

    [Fact]
    public async Task MergeAsync_ReassignsEditionsAndDeletesSource()
    {
        var factory = new TestDbContextFactory();
        int sourceId;
        int targetId;
        using (var db = factory.CreateDbContext())
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

        var vm = new PublisherListViewModel(factory);
        await vm.LoadAsync();
        await vm.MergeAsync(sourceId, targetId);

        using var db2 = factory.CreateDbContext();
        Assert.Null(db2.Publishers.FirstOrDefault(p => p.Id == sourceId));
        Assert.Equal(2, db2.Editions.Count(e => e.PublisherId == targetId));
        Assert.Contains("reassigned", vm.SuccessMessage);
    }

    [Fact]
    public async Task MergeAsync_NoOpWhenSourceEqualsTarget()
    {
        var factory = new TestDbContextFactory();
        int publisherId;
        using (var db = factory.CreateDbContext())
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

        var vm = new PublisherListViewModel(factory);
        await vm.LoadAsync();
        await vm.MergeAsync(publisherId, publisherId);

        using var db2 = factory.CreateDbContext();
        Assert.NotNull(db2.Publishers.FirstOrDefault(p => p.Id == publisherId));
        Assert.Equal(1, db2.Editions.Count(e => e.PublisherId == publisherId));
    }
}
