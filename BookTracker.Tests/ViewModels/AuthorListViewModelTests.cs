using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Integration)]
public class AuthorListViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesAuthorRows_WithCanonicalAndAliasShape()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            db.Authors.AddRange(king, bachman);
            db.Books.Add(new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "Thinner", Works = [new Work { Title = "Thinner", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        var vm = new AuthorListViewModel(factory);
        await vm.LoadAsync();

        Assert.Equal(2, vm.Authors.Count);
        var kingRow = vm.Authors.Single(a => a.Name == "Stephen King");
        Assert.Null(kingRow.CanonicalAuthorId);
        Assert.Contains("Richard Bachman", kingRow.AliasNames);

        var bachmanRow = vm.Authors.Single(a => a.Name == "Richard Bachman");
        Assert.Equal(kingRow.Id, bachmanRow.CanonicalAuthorId);
    }

    [Fact]
    public async Task LoadAsync_CanonicalCountsRollUpAliasWorks_AliasCountsAreOwnOnly()
    {
        // King has Carrie + IT (own); Bachman is an alias contributing Thinner.
        // King's row should report 3 works / 3 books / 0 series. Bachman's row
        // should report just its own — 1 / 1 / 0.
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            db.Authors.AddRange(king, bachman);
            db.Books.Add(new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "It", Works = [new Work { Title = "It", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "Thinner", Works = [new Work { Title = "Thinner", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        var vm = new AuthorListViewModel(factory);
        await vm.LoadAsync();

        var kingRow = vm.Authors.Single(a => a.Name == "Stephen King");
        Assert.Equal(3, kingRow.WorkCount);
        Assert.Equal(3, kingRow.BookCount);
        Assert.Equal(0, kingRow.SeriesCount);

        var bachmanRow = vm.Authors.Single(a => a.Name == "Richard Bachman");
        Assert.Equal(1, bachmanRow.WorkCount);
        Assert.Equal(1, bachmanRow.BookCount);
        Assert.Equal(0, bachmanRow.SeriesCount);
    }

    [Fact]
    public async Task LoadAsync_SeriesCount_DistinctSeriesAcrossWorks()
    {
        // Pratchett: Discworld + Bromeliad + a standalone. Series count = 2.
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var pratchett = new Author { Name = "Terry Pratchett" };
            db.Authors.Add(pratchett);
            var discworld = new Series { Name = "Discworld", Type = SeriesType.Collection };
            var bromeliad = new Series { Name = "Bromeliad", Type = SeriesType.Series };
            db.Series.AddRange(discworld, bromeliad);

            db.Books.AddRange(
                new Book { Title = "Mort", Works = [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 4 }] },
                new Book { Title = "The Colour of Magic", Works = [new Work { Title = "The Colour of Magic", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 1 }] },
                new Book { Title = "Truckers", Works = [new Work { Title = "Truckers", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = bromeliad, SeriesOrder = 1 }] },
                new Book { Title = "Good Omens", Works = [new Work { Title = "Good Omens", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        var vm = new AuthorListViewModel(factory);
        await vm.LoadAsync();

        var p = vm.Authors.Single(a => a.Name == "Terry Pratchett");
        Assert.Equal(4, p.WorkCount);
        Assert.Equal(4, p.BookCount);
        Assert.Equal(2, p.SeriesCount);
    }

    [Fact]
    public async Task FilteredAuthors_HidesAliases_WhenShowAliasesIsFalse()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            db.Authors.AddRange(king, bachman);
            await db.SaveChangesAsync();
        }

        var vm = new AuthorListViewModel(factory);
        await vm.LoadAsync();
        Assert.Equal(2, vm.FilteredAuthors.Count());

        vm.ShowAliases = false;
        var only = Assert.Single(vm.FilteredAuthors);
        Assert.Equal("Stephen King", only.Name);
    }

    [Fact]
    public async Task FilteredAuthors_SearchIsAliasAware_AndCaseInsensitive()
    {
        // Typing "bachman" should surface King's row even with show-aliases off,
        // because the alias name contains the term.
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            var atwood = new Author { Name = "Margaret Atwood" };
            db.Authors.AddRange(king, bachman, atwood);
            await db.SaveChangesAsync();
        }

        var vm = new AuthorListViewModel(factory);
        await vm.LoadAsync();

        vm.SearchTerm = "bachman";
        var matches = vm.FilteredAuthors.Select(a => a.Name).ToList();
        Assert.Contains("Stephen King", matches);   // matched by alias rollup
        Assert.Contains("Richard Bachman", matches); // matched by literal name
        Assert.DoesNotContain("Margaret Atwood", matches);

        // Show-aliases=false + alias-name search still surfaces the canonical.
        vm.ShowAliases = false;
        var canonicalOnly = vm.FilteredAuthors.Select(a => a.Name).ToList();
        Assert.Equal(["Stephen King"], canonicalOnly);
    }

    [Fact]
    public async Task FilteredAuthors_EmptySearch_ReturnsAllRows()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            db.Authors.AddRange(
                new Author { Name = "A" },
                new Author { Name = "B" });
            await db.SaveChangesAsync();
        }

        var vm = new AuthorListViewModel(factory);
        await vm.LoadAsync();

        Assert.Equal(2, vm.FilteredAuthors.Count());
        vm.SearchTerm = "   ";
        Assert.Equal(2, vm.FilteredAuthors.Count()); // whitespace ignored
    }
}
