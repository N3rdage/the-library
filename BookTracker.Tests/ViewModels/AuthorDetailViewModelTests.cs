using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Integration)]
public class AuthorDetailViewModelTests
{
    [Fact]
    public async Task LoadAsync_NonExistentId_SetsNotFound()
    {
        var factory = new TestDbContextFactory();
        var vm = new AuthorDetailViewModel(factory);

        await vm.LoadAsync(99999);

        Assert.True(vm.NotFound);
        Assert.Null(vm.Header);
    }

    [Fact]
    public async Task LoadAsync_CanonicalRollsUpAliasWorks()
    {
        var factory = new TestDbContextFactory();
        int kingId;
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            db.Authors.AddRange(king, bachman);
            db.Books.Add(new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "Thinner", Works = [new Work { Title = "Thinner", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
            await db.SaveChangesAsync();
            kingId = king.Id;
        }

        var vm = new AuthorDetailViewModel(factory);
        await vm.LoadAsync(kingId);

        Assert.NotNull(vm.Header);
        Assert.Equal("Stephen King", vm.Header.Name);
        Assert.Equal(2, vm.Detail.Works.Count);
        Assert.Contains(vm.Detail.Works, w => w.Title == "Carrie");
        Assert.Contains(vm.Detail.Works, w => w.Title == "Thinner");
        Assert.Contains("Richard Bachman", vm.Detail.AliasNames);

        // Bachman work flagged with WrittenAs; King work isn't.
        Assert.Equal("Richard Bachman", vm.Detail.Works.Single(w => w.Title == "Thinner").WrittenAs);
        Assert.Null(vm.Detail.Works.Single(w => w.Title == "Carrie").WrittenAs);
    }

    [Fact]
    public async Task LoadAsync_AliasShowsOwnWorksOnly()
    {
        var factory = new TestDbContextFactory();
        int bachmanId;
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            db.Authors.AddRange(king, bachman);
            db.Books.Add(new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "Thinner", Works = [new Work { Title = "Thinner", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
            await db.SaveChangesAsync();
            bachmanId = bachman.Id;
        }

        var vm = new AuthorDetailViewModel(factory);
        await vm.LoadAsync(bachmanId);

        Assert.Single(vm.Detail.Works);
        Assert.Equal("Thinner", vm.Detail.Works[0].Title);
        Assert.Empty(vm.Detail.AliasNames);
        Assert.Null(vm.Detail.Works[0].WrittenAs);
    }

    [Fact]
    public async Task LoadAsync_OrdersWorksByInSeriesThenSeriesOrderThenTitle()
    {
        // Same fixture as the previous AuthorListViewModelTests Pratchett case;
        // Discworld 1, 2, 3, ... clusters before Bromeliad alphabetically, then
        // standalones tail at the end.
        var factory = new TestDbContextFactory();
        int authorId;
        using (var db = factory.CreateDbContext())
        {
            var pratchett = new Author { Name = "Terry Pratchett" };
            db.Authors.Add(pratchett);
            var discworld = new Series { Name = "Discworld", Type = SeriesType.Collection };
            var bromeliad = new Series { Name = "Bromeliad", Type = SeriesType.Series };
            db.Series.AddRange(discworld, bromeliad);

            db.Books.AddRange(
                new Book { Title = "Good Omens", Works = [new Work { Title = "Good Omens", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] },
                new Book { Title = "Nation", Works = [new Work { Title = "Nation", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] },
                new Book { Title = "Mort", Works = [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 4 }] },
                new Book { Title = "The Colour of Magic", Works = [new Work { Title = "The Colour of Magic", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 1 }] },
                new Book { Title = "Equal Rites", Works = [new Work { Title = "Equal Rites", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 3 }] },
                new Book { Title = "Truckers", Works = [new Work { Title = "Truckers", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = bromeliad, SeriesOrder = 1 }] });
            await db.SaveChangesAsync();
            authorId = pratchett.Id;
        }

        var vm = new AuthorDetailViewModel(factory);
        await vm.LoadAsync(authorId);

        var titles = vm.Detail.Works.Select(w => w.Title).ToList();
        Assert.Equal(
            ["Truckers", "The Colour of Magic", "Equal Rites", "Mort", "Good Omens", "Nation"],
            titles);
    }

    [Fact]
    public async Task RenameAsync_UpdatesNameAndReloads()
    {
        var factory = new TestDbContextFactory();
        int authorId;
        using (var db = factory.CreateDbContext())
        {
            var a = new Author { Name = "Old Name" };
            db.Authors.Add(a);
            await db.SaveChangesAsync();
            authorId = a.Id;
        }

        var vm = new AuthorDetailViewModel(factory);
        await vm.LoadAsync(authorId);
        await vm.RenameAsync("New Name");

        Assert.NotNull(vm.Header);
        Assert.Equal("New Name", vm.Header.Name);
        Assert.NotNull(vm.SuccessMessage);
    }

    [Fact]
    public async Task RenameAsync_RejectsNameClash()
    {
        var factory = new TestDbContextFactory();
        int aliceId;
        using (var db = factory.CreateDbContext())
        {
            db.Authors.AddRange(new Author { Name = "Alice" }, new Author { Name = "Bob" });
            await db.SaveChangesAsync();
            aliceId = db.Authors.Single(a => a.Name == "Alice").Id;
        }

        var vm = new AuthorDetailViewModel(factory);
        await vm.LoadAsync(aliceId);
        await vm.RenameAsync("Bob");

        Assert.NotNull(vm.Header);
        Assert.Equal("Alice", vm.Header.Name); // unchanged
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task MarkAsAliasOfAsync_LinksToCanonicalAndRefreshesHeader()
    {
        var factory = new TestDbContextFactory();
        int kingId;
        int bachmanId;
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman" };
            db.Authors.AddRange(king, bachman);
            await db.SaveChangesAsync();
            kingId = king.Id;
            bachmanId = bachman.Id;
        }

        var vm = new AuthorDetailViewModel(factory);
        await vm.LoadAsync(bachmanId);
        await vm.MarkAsAliasOfAsync(kingId);

        Assert.NotNull(vm.Header);
        Assert.Equal(kingId, vm.Header.CanonicalAuthorId);
        Assert.Equal("Stephen King", vm.Header.CanonicalName);
    }

    [Fact]
    public async Task MarkAsAliasOfAsync_ChainedTarget_ReRootsToTopCanonical()
    {
        // If A is an alias of B, and we mark C as alias-of-A, C should
        // actually point at B (no two-hop chains).
        var factory = new TestDbContextFactory();
        int aId;
        int bId;
        int cId;
        using (var db = factory.CreateDbContext())
        {
            var b = new Author { Name = "B" };
            var a = new Author { Name = "A", CanonicalAuthor = b };
            var c = new Author { Name = "C" };
            db.Authors.AddRange(b, a, c);
            await db.SaveChangesAsync();
            aId = a.Id;
            bId = b.Id;
            cId = c.Id;
        }

        var vm = new AuthorDetailViewModel(factory);
        await vm.LoadAsync(cId);
        await vm.MarkAsAliasOfAsync(aId);

        Assert.NotNull(vm.Header);
        Assert.Equal(bId, vm.Header.CanonicalAuthorId);
    }

    [Fact]
    public async Task PromoteToCanonicalAsync_DropsCanonicalLink()
    {
        var factory = new TestDbContextFactory();
        int bachmanId;
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            db.Authors.AddRange(king, bachman);
            await db.SaveChangesAsync();
            bachmanId = bachman.Id;
        }

        var vm = new AuthorDetailViewModel(factory);
        await vm.LoadAsync(bachmanId);
        await vm.PromoteToCanonicalAsync();

        Assert.NotNull(vm.Header);
        Assert.Null(vm.Header.CanonicalAuthorId);
    }
}
