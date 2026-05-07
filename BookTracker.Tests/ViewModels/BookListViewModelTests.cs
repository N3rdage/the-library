using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

// Focused on the grouping behaviour added with the library-groupings PR.
// The flat-list path is exercised indirectly elsewhere; here we cover the
// new GroupBy enum + canonical-author rollup + (no genre)/(no series)
// trailing buckets.
[Trait("Category", TestCategories.Integration)]
public class BookListViewModelTests
{
    private static async Task SeedSampleLibraryAsync(TestDbContextFactory factory)
    {
        using var db = factory.CreateDbContext();

        // Authors: Stephen King canonical, Richard Bachman as alias.
        var king = new Author { Name = "Stephen King" };
        db.Authors.Add(king);
        await db.SaveChangesAsync();

        var bachman = new Author { Name = "Richard Bachman", CanonicalAuthorId = king.Id };
        var christie = new Author { Name = "Agatha Christie" };
        db.Authors.AddRange(bachman, christie);

        var horror = new Genre { Name = "Horror" };
        var mystery = new Genre { Name = "Mystery" };
        db.Genres.AddRange(horror, mystery);

        var poirot = new Series { Name = "Hercule Poirot", Type = SeriesType.Collection };
        db.Series.Add(poirot);

        db.Books.AddRange(
            new Book
            {
                Title = "Carrie",
                Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }], Genres = [horror] }]
            },
            new Book
            {
                Title = "The Long Walk",
                Works = [new Work { Title = "The Long Walk", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }], Genres = [horror] }]
            },
            new Book
            {
                Title = "Murder on the Orient Express",
                Works = [new Work { Title = "Murder on the Orient Express", WorkAuthors = [new WorkAuthor { Author = christie, Order = 0 }], Genres = [mystery], Series = poirot, SeriesOrder = 9 }]
            },
            new Book
            {
                // No genre, no series — exercises the trailing buckets.
                Title = "Mystery Book Without Tags",
                Works = [new Work { Title = "Mystery Book Without Tags", WorkAuthors = [new WorkAuthor { Author = christie, Order = 0 }] }]
            });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GroupByAuthor_RollsAliasesUnderCanonical()
    {
        var factory = new TestDbContextFactory();
        await SeedSampleLibraryAsync(factory);

        var vm = new BookListViewModel(factory) { SelectedGroupBy = LibraryGroupBy.Author };
        await vm.InitializeAsync();

        // Christie has 2 books; King has 2 (Carrie + Bachman's The Long Walk
        // rolled up). Expect exactly two author groups, no Bachman row.
        Assert.Equal(2, vm.Groups.Count);
        Assert.Contains(vm.Groups, g => g.Label == "Stephen King" && g.Count == 2);
        Assert.Contains(vm.Groups, g => g.Label == "Agatha Christie" && g.Count == 2);
        Assert.DoesNotContain(vm.Groups, g => g.Label == "Richard Bachman");
    }

    [Fact]
    public async Task GroupByGenre_AppendsNoGenreBucket()
    {
        var factory = new TestDbContextFactory();
        await SeedSampleLibraryAsync(factory);

        var vm = new BookListViewModel(factory) { SelectedGroupBy = LibraryGroupBy.Genre };
        await vm.InitializeAsync();

        // Horror: 2 (Carrie, The Long Walk). Mystery: 1 (Orient Express).
        // Plus the no-genre book in the trailing "(no genre)" bucket.
        Assert.Contains(vm.Groups, g => g.Label == "Horror" && g.Count == 2);
        Assert.Contains(vm.Groups, g => g.Label == "Mystery" && g.Count == 1);
        Assert.Contains(vm.Groups, g => g.Label == "(no genre)" && g.Count == 1);
        // The no-genre bucket should be last.
        Assert.Equal("(no genre)", vm.Groups.Last().Label);
    }

    [Fact]
    public async Task GroupByCollection_AppendsNoSeriesBucket()
    {
        var factory = new TestDbContextFactory();
        await SeedSampleLibraryAsync(factory);

        var vm = new BookListViewModel(factory) { SelectedGroupBy = LibraryGroupBy.Collection };
        await vm.InitializeAsync();

        Assert.Contains(vm.Groups, g => g.Label == "Hercule Poirot" && g.Count == 1);
        Assert.Contains(vm.Groups, g => g.Label == "(no series)" && g.Count == 3);
        Assert.Equal("(no series)", vm.Groups.Last().Label);
    }

    [Fact]
    public async Task ToggleGroupAsync_ExpandsAndLoadsBooks()
    {
        var factory = new TestDbContextFactory();
        await SeedSampleLibraryAsync(factory);

        var vm = new BookListViewModel(factory) { SelectedGroupBy = LibraryGroupBy.Author };
        await vm.InitializeAsync();

        var kingGroup = vm.Groups.First(g => g.Label == "Stephen King");
        await vm.ToggleGroupAsync(kingGroup.Key);

        Assert.Contains(kingGroup.Key, vm.ExpandedGroupKeys);
        var loaded = vm.LoadedGroups[kingGroup.Key];
        Assert.Equal(2, loaded.TotalCount);
        // Includes the Bachman alias title.
        Assert.Contains(loaded.Books, b => b.Title == "The Long Walk");
        Assert.Contains(loaded.Books, b => b.Title == "Carrie");

        // Toggling again collapses without reloading.
        await vm.ToggleGroupAsync(kingGroup.Key);
        Assert.DoesNotContain(kingGroup.Key, vm.ExpandedGroupKeys);
    }

    [Fact]
    public async Task ToggleGroupAsync_AuthorGroup_ClustersSeriesThenStandaloneAlphabetical()
    {
        // Mirrors the /authors expand fix: when the Library is grouped by
        // Author and expanded, books inside the group should cluster their
        // series-having members first (in series-then-SeriesOrder order)
        // and tail standalone books alphabetical. Pure title sort buried
        // Drew's Discworld ordering on this view.
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

        var vm = new BookListViewModel(factory) { SelectedGroupBy = LibraryGroupBy.Author };
        await vm.InitializeAsync();
        var pratchettGroup = vm.Groups.First(g => g.Label == "Terry Pratchett");
        await vm.ToggleGroupAsync(pratchettGroup.Key);

        var titles = vm.LoadedGroups[pratchettGroup.Key].Books.Select(b => b.Title).ToList();
        Assert.Equal(
            ["Truckers", "The Colour of Magic", "Equal Rites", "Mort", "Good Omens", "Nation"],
            titles);
    }

    [Fact]
    public async Task ToggleGroupAsync_CollectionGroup_OrdersBooksBySeriesOrder()
    {
        // Group by Collection then expand a series — books should appear in
        // SeriesOrder, not title-alphabetical. Title-only sort would have
        // hidden Drew's manually-set Discworld order on the Library page.
        var factory = new TestDbContextFactory();
        int seriesId;
        using (var db = factory.CreateDbContext())
        {
            var pratchett = new Author { Name = "Terry Pratchett" };
            db.Authors.Add(pratchett);
            var discworld = new Series { Name = "Discworld", Type = SeriesType.Collection };
            db.Series.Add(discworld);

            db.Books.AddRange(
                // Title-alphabet vs SeriesOrder are deliberately reversed so a
                // title-only sort would produce the wrong result.
                new Book { Title = "Mort", Works = [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 4 }] },
                new Book { Title = "Equal Rites", Works = [new Work { Title = "Equal Rites", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 3 }] },
                new Book { Title = "The Colour of Magic", Works = [new Work { Title = "The Colour of Magic", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 1 }] });
            await db.SaveChangesAsync();
            seriesId = discworld.Id;
        }

        var vm = new BookListViewModel(factory) { SelectedGroupBy = LibraryGroupBy.Collection };
        await vm.InitializeAsync();
        var seriesGroup = vm.Groups.First(g => g.Label == "Discworld");
        await vm.ToggleGroupAsync(seriesGroup.Key);

        var titles = vm.LoadedGroups[seriesGroup.Key].Books.Select(b => b.Title).ToList();
        Assert.Equal(["The Colour of Magic", "Equal Rites", "Mort"], titles);
    }

    [Fact]
    public async Task ToggleGroupAsync_MultiWorkBook_SuppressesSubtitleAndReportsWorkCount()
    {
        // For collections (Book.Works.Count > 1), the subtitle of an arbitrary
        // inner Work is data noise — list view shows "N works" instead.
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            db.Authors.Add(king);

            db.Books.AddRange(
                new Book
                {
                    // Single-work book — its Work subtitle should surface.
                    Title = "The Shining",
                    Works = [new Work
                    {
                        Title = "The Shining",
                        Subtitle = "A Novel",
                        WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }]
                    }]
                },
                new Book
                {
                    // Multi-work collection — even though Work[0] has a subtitle,
                    // the list should suppress it and report WorkCount=3.
                    Title = "The Bachman Books",
                    Works =
                    [
                        new Work { Title = "Rage", Subtitle = "Subtitle from a single story", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] },
                        new Work { Title = "The Long Walk", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] },
                        new Work { Title = "Roadwork", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] },
                    ]
                });
            await db.SaveChangesAsync();
        }

        var vm = new BookListViewModel(factory) { SelectedGroupBy = LibraryGroupBy.Author };
        await vm.InitializeAsync();
        var kingGroup = vm.Groups.First(g => g.Label == "Stephen King");
        await vm.ToggleGroupAsync(kingGroup.Key);

        var loaded = vm.LoadedGroups[kingGroup.Key].Books;
        var single = loaded.Single(b => b.Title == "The Shining");
        var collection = loaded.Single(b => b.Title == "The Bachman Books");

        Assert.Equal("A Novel", single.Subtitle);
        Assert.Equal(1, single.WorkCount);
        Assert.Null(collection.Subtitle);
        Assert.Equal(3, collection.WorkCount);
    }

    [Fact]
    public async Task GroupByGenre_GenreFilterReducesGroupsAndCounts()
    {
        var factory = new TestDbContextFactory();
        await SeedSampleLibraryAsync(factory);

        Genre mystery;
        using (var db = factory.CreateDbContext())
        {
            mystery = db.Genres.Single(g => g.Name == "Mystery");
        }

        var vm = new BookListViewModel(factory)
        {
            SelectedGroupBy = LibraryGroupBy.Genre,
            SelectedGenreId = mystery.Id,
        };
        await vm.InitializeAsync();

        // Filtering to Mystery should leave only the Mystery group; no
        // (no genre) trailing bucket because the no-genre book doesn't
        // pass the filter.
        Assert.Single(vm.Groups);
        Assert.Equal("Mystery", vm.Groups[0].Label);
        Assert.Equal(1, vm.Groups[0].Count);
    }
}
