using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

public class AuthorListViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesAuthorRows()
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
        var bachmanRow = vm.Authors.Single(a => a.Name == "Richard Bachman");
        Assert.Equal(kingRow.Id, bachmanRow.CanonicalAuthorId);
    }

    [Fact]
    public async Task ToggleExpandAsync_CanonicalRollsUpAliasWorks()
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

        var vm = new AuthorListViewModel(factory);
        await vm.LoadAsync();
        await vm.ToggleExpandAsync(kingId);

        Assert.Contains(kingId, vm.ExpandedAuthorIds);
        var detail = vm.DetailByAuthorId[kingId];
        Assert.Equal(2, detail.Works.Count);
        Assert.Contains(detail.Works, w => w.Title == "Carrie");
        Assert.Contains(detail.Works, w => w.Title == "Thinner");
        Assert.Contains("Richard Bachman", detail.AliasNames);

        // The Bachman work should be flagged with WrittenAs, the King one shouldn't.
        Assert.Equal("Richard Bachman", detail.Works.Single(w => w.Title == "Thinner").WrittenAs);
        Assert.Null(detail.Works.Single(w => w.Title == "Carrie").WrittenAs);
    }

    [Fact]
    public async Task ToggleExpandAsync_AliasRowShowsOnlyOwnWorks()
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

        var vm = new AuthorListViewModel(factory);
        await vm.LoadAsync();
        await vm.ToggleExpandAsync(bachmanId);

        var detail = vm.DetailByAuthorId[bachmanId];
        Assert.Single(detail.Works);
        Assert.Equal("Thinner", detail.Works[0].Title);
        Assert.Empty(detail.AliasNames); // alias rows don't roll anything up
        Assert.Null(detail.Works[0].WrittenAs); // no "as X" label on alias-own rows
    }

    [Fact]
    public async Task ToggleExpandAsync_SecondCallCollapsesWithoutReload()
    {
        var factory = new TestDbContextFactory();
        int authorId;
        using (var db = factory.CreateDbContext())
        {
            var author = new Author { Name = "A" };
            db.Authors.Add(author);
            db.Books.Add(new Book { Title = "B", Works = [new Work { Title = "W", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }] });
            await db.SaveChangesAsync();
            authorId = author.Id;
        }

        var vm = new AuthorListViewModel(factory);
        await vm.LoadAsync();
        await vm.ToggleExpandAsync(authorId);
        Assert.Contains(authorId, vm.ExpandedAuthorIds);
        Assert.True(vm.DetailByAuthorId.ContainsKey(authorId));

        await vm.ToggleExpandAsync(authorId);
        Assert.DoesNotContain(authorId, vm.ExpandedAuthorIds);
        // Detail cache survives the collapse — a later re-expand reuses it.
        Assert.True(vm.DetailByAuthorId.ContainsKey(authorId));
    }

    [Fact]
    public async Task GetViewMode_DefaultsToWorks_SetViewMode_Sticks()
    {
        var factory = new TestDbContextFactory();
        var vm = new AuthorListViewModel(factory);

        Assert.Equal(AuthorListViewModel.AuthorViewMode.Works, vm.GetViewMode(42));
        vm.SetViewMode(42, AuthorListViewModel.AuthorViewMode.Books);
        Assert.Equal(AuthorListViewModel.AuthorViewMode.Books, vm.GetViewMode(42));
    }

    [Fact]
    public async Task ToggleExpandAsync_OrdersWorksByInSeries_ThenSeriesOrder_ThenTitle()
    {
        // Drew set up Discworld manually with SeriesOrder 1..N and expects
        // the /authors expand to read Discworld 1, 2, 3, ..., then standalone
        // works alphabetical — not pure title-alphabetical (which buried the
        // numbered series order entirely). This test fixes that ordering.
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
                // Standalone (no series) — expect at the END despite alpha-early titles.
                new Book { Title = "Good Omens", Works = [new Work { Title = "Good Omens", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] },
                new Book { Title = "Nation", Works = [new Work { Title = "Nation", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] },
                // Discworld — out-of-order titles to prove SeriesOrder wins over Title.
                new Book { Title = "Mort", Works = [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 4 }] },
                new Book { Title = "The Colour of Magic", Works = [new Work { Title = "The Colour of Magic", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 1 }] },
                new Book { Title = "Equal Rites", Works = [new Work { Title = "Equal Rites", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 3 }] },
                // Bromeliad — single work; cluster comes BEFORE Discworld alphabetically.
                new Book { Title = "Truckers", Works = [new Work { Title = "Truckers", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = bromeliad, SeriesOrder = 1 }] });
            await db.SaveChangesAsync();
            authorId = pratchett.Id;
        }

        var vm = new AuthorListViewModel(factory);
        await vm.LoadAsync();
        await vm.ToggleExpandAsync(authorId);

        var titles = vm.DetailByAuthorId[authorId].Works.Select(w => w.Title).ToList();
        Assert.Equal(
            ["Truckers", "The Colour of Magic", "Equal Rites", "Mort", "Good Omens", "Nation"],
            titles);
    }

    [Fact]
    public async Task MarkAsAliasAsync_InvalidatesDetailCache()
    {
        // Structural change (mark X as alias of Y) should drop any cached
        // detail for X and Y so the next expand picks up the new roll-up.
        var factory = new TestDbContextFactory();
        int kingId;
        int bachmanId;
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman" }; // NOT an alias yet
            db.Authors.AddRange(king, bachman);
            db.Books.Add(new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "Thinner", Works = [new Work { Title = "Thinner", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
            await db.SaveChangesAsync();
            kingId = king.Id;
            bachmanId = bachman.Id;
        }

        var vm = new AuthorListViewModel(factory);
        await vm.LoadAsync();
        await vm.ToggleExpandAsync(kingId);
        Assert.Single(vm.DetailByAuthorId[kingId].Works); // Carrie only

        await vm.MarkAsAliasAsync(bachmanId, kingId);

        Assert.False(vm.DetailByAuthorId.ContainsKey(kingId));
        await vm.ExpandAsync(kingId);
        Assert.Equal(2, vm.DetailByAuthorId[kingId].Works.Count); // now includes Thinner
    }
}
