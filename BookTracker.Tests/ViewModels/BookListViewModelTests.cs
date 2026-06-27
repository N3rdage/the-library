using BookTracker.Application.Books;
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

        var vm = new BookListViewModel(TestDispatcher.For(factory)) { SelectedGroupBy = LibraryGroupBy.Author };
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

        var vm = new BookListViewModel(TestDispatcher.For(factory)) { SelectedGroupBy = LibraryGroupBy.Genre };
        await vm.InitializeAsync();

        // Horror: 2 (Carrie, The Long Walk). Mystery: 1 (Orient Express).
        // Plus the no-genre book in the trailing "(no genre)" bucket.
        Assert.Contains(vm.Groups, g => g.Label == "Horror" && g.Count == 2);
        Assert.Contains(vm.Groups, g => g.Label == "Mystery" && g.Count == 1);
        Assert.Contains(vm.Groups, g => g.Label == "(no genre)" && g.Count == 1);
        // The no-genre bucket should be last.
        Assert.Equal("(no genre)", vm.Groups.Last().Label);
    }

    // Note: there is no "group by series" mode — series browsing is the
    // Series filter + reading-order sort (FlatList_SeriesFilter_* below),
    // which retired the grouped Series view (TODO #53c).

    [Fact]
    public async Task FlatList_SeriesFilter_ReturnsOnlyThatSeries()
    {
        var factory = new TestDbContextFactory();
        await SeedSampleLibraryAsync(factory);

        int poirotId;
        using (var db = factory.CreateDbContext())
            poirotId = db.Series.Single(s => s.Name == "Hercule Poirot").Id;

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.None,
            SelectedSeriesId = poirotId,
        };
        await vm.InitializeAsync();

        Assert.Equal(1, vm.TotalCount);
        Assert.Equal("Murder on the Orient Express", Assert.Single(vm.Books).Title);
    }

    [Fact]
    public async Task FlatList_NoSeriesFilter_ReturnsSerieslessBooks()
    {
        var factory = new TestDbContextFactory();
        await SeedSampleLibraryAsync(factory);

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.None,
            SelectedSeriesId = -1, // "(no series)"
        };
        await vm.InitializeAsync();

        // Everything except the one Poirot book.
        Assert.Equal(3, vm.TotalCount);
        Assert.DoesNotContain(vm.Books, b => b.Title == "Murder on the Orient Express");
        Assert.Contains(vm.Books, b => b.Title == "Carrie");
        Assert.Contains(vm.Books, b => b.Title == "The Long Walk");
        Assert.Contains(vm.Books, b => b.Title == "Mystery Book Without Tags");
    }

    [Fact]
    public async Task FlatList_NoGenreFilter_ReturnsUngenredBooks()
    {
        var factory = new TestDbContextFactory();
        await SeedSampleLibraryAsync(factory);

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.None,
            SelectedGenreId = -1, // "(no genre)"
        };
        await vm.InitializeAsync();

        Assert.Equal("Mystery Book Without Tags", Assert.Single(vm.Books).Title);
    }

    [Fact]
    public async Task FlatList_SeriesFilter_SortsBySeriesOrder()
    {
        // Drilling into a series shows reading order, not DateAdded. Seed the
        // books out of order to prove the SeriesOrder sort is what's applied.
        var factory = new TestDbContextFactory();
        int seriesId;
        using (var db = factory.CreateDbContext())
        {
            var herbert = new Author { Name = "Frank Herbert" };
            db.Authors.Add(herbert);
            var dune = new Series { Name = "Dune", Type = SeriesType.Series };
            db.Series.Add(dune);
            db.Books.AddRange(
                new Book { Title = "Dune Messiah", Works = [new Work { Title = "Dune Messiah", WorkAuthors = [new WorkAuthor { Author = herbert, Order = 0 }], Series = dune, SeriesOrder = 2 }] },
                new Book { Title = "Dune", Works = [new Work { Title = "Dune", WorkAuthors = [new WorkAuthor { Author = herbert, Order = 0 }], Series = dune, SeriesOrder = 1 }] },
                new Book { Title = "Children of Dune", Works = [new Work { Title = "Children of Dune", WorkAuthors = [new WorkAuthor { Author = herbert, Order = 0 }], Series = dune, SeriesOrder = 3 }] });
            await db.SaveChangesAsync();
            seriesId = dune.Id;
        }

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.None,
            SelectedSeriesId = seriesId,
        };
        await vm.InitializeAsync();

        Assert.Equal(
            ["Dune", "Dune Messiah", "Children of Dune"],
            vm.Books.Select(b => b.Title).ToList());
    }

    [Fact]
    public async Task SeriesFilter_ForcesFlatReadingOrderList_EvenUnderAuthorGrouping()
    {
        // TODO #53c: a specific series filter shows the reading-order flat list
        // regardless of the Group-by selection (it replaced the retired Series
        // grouping). Default grouping is Author; picking a series must still
        // land on the flat, SeriesOrder-sorted list — not author groups.
        var factory = new TestDbContextFactory();
        int seriesId;
        using (var db = factory.CreateDbContext())
        {
            var herbert = new Author { Name = "Frank Herbert" };
            var dune = new Series { Name = "Dune", Type = SeriesType.Series };
            db.Books.AddRange(
                new Book { Title = "Dune Messiah", Works = [new Work { Title = "Dune Messiah", WorkAuthors = [new WorkAuthor { Author = herbert, Order = 0 }], Series = dune, SeriesOrder = 2 }] },
                new Book { Title = "Dune", Works = [new Work { Title = "Dune", WorkAuthors = [new WorkAuthor { Author = herbert, Order = 0 }], Series = dune, SeriesOrder = 1 }] });
            await db.SaveChangesAsync();
            seriesId = dune.Id;
        }

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.Author, // the default — NOT None
            SelectedSeriesId = seriesId,
        };
        await vm.InitializeAsync();

        Assert.True(vm.ShowingFlatList);
        Assert.Empty(vm.Groups); // no grouped rows produced under a series filter
        Assert.Equal(["Dune", "Dune Messiah"], vm.Books.Select(b => b.Title).ToList()); // reading order
    }

    [Fact]
    public async Task FlatList_SeriesFilter_BookWithTwoWorksInSameSeries_AppearsOnce()
    {
        // A compendium Book can contain two Works of the same series (an omnibus
        // of #1 + #2). The series-filtered flat list must show that Book ONCE,
        // not once per matching Work — the Where(b.Works.Any(...)) shape avoids
        // a SelectMany fan-out. Guards against a future per-Work join sneaking
        // duplicates back in (the dedup the retired grouping did via .Distinct).
        var factory = new TestDbContextFactory();
        int seriesId;
        using (var db = factory.CreateDbContext())
        {
            var herbert = new Author { Name = "Frank Herbert" };
            var dune = new Series { Name = "Dune", Type = SeriesType.Series };
            db.Books.Add(new Book
            {
                Title = "Dune Omnibus",
                Works =
                [
                    new Work { Title = "Dune", WorkAuthors = [new WorkAuthor { Author = herbert, Order = 0 }], Series = dune, SeriesOrder = 1 },
                    new Work { Title = "Dune Messiah", WorkAuthors = [new WorkAuthor { Author = herbert, Order = 0 }], Series = dune, SeriesOrder = 2 },
                ],
            });
            await db.SaveChangesAsync();
            seriesId = dune.Id;
        }

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.None,
            SelectedSeriesId = seriesId,
        };
        await vm.InitializeAsync();

        Assert.Equal(1, vm.TotalCount);
        Assert.Equal("Dune Omnibus", Assert.Single(vm.Books).Title);
    }

    [Fact]
    public async Task FlatList_PageBeyondRange_ClampsToLastPage()
    {
        // A page number past the end (e.g. a stale bookmarked ?page=5 after the
        // library shrank) clamps to the last page. The page then writes the
        // corrected value back into the URL (handled in List.razor).
        var factory = new TestDbContextFactory();
        await SeedSampleLibraryAsync(factory); // 4 books → 1 page

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.None,
            CurrentPage = 5,
        };
        await vm.InitializeAsync();

        Assert.Equal(1, vm.TotalPages);
        Assert.Equal(1, vm.CurrentPage);
    }

    [Fact]
    public async Task FlatList_AuthorFilter_IncludesAliasRollup()
    {
        // Drilling an Author group lands on the flat list filtered by the
        // canonical author name; the filter rolls up aliases (Bachman → King).
        var factory = new TestDbContextFactory();
        await SeedSampleLibraryAsync(factory);

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.None,
            SelectedAuthor = "Stephen King",
        };
        await vm.InitializeAsync();

        Assert.Equal(2, vm.TotalCount);
        Assert.Contains(vm.Books, b => b.Title == "Carrie");
        Assert.Contains(vm.Books, b => b.Title == "The Long Walk"); // Bachman alias rolled up
    }

    [Fact]
    public async Task FlatList_MultiWorkBook_SuppressesSubtitleAndReportsWorkCount()
    {
        // For collections (Book.Works.Count > 1), the subtitle of an arbitrary
        // inner Work is data noise — list view shows "N works" instead. (Shaping
        // lives in ToBookListItem, shared by the flat list and any filtered view.)
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

        var vm = new BookListViewModel(TestDispatcher.For(factory)) { SelectedGroupBy = LibraryGroupBy.None };
        await vm.InitializeAsync();

        var single = vm.Books.Single(b => b.Title == "The Shining");
        var collection = vm.Books.Single(b => b.Title == "The Bachman Books");

        Assert.Equal("A Novel", single.Subtitle);
        Assert.Equal(1, single.WorkCount);
        Assert.Null(collection.Subtitle);
        Assert.Equal(3, collection.WorkCount);
    }

    [Fact]
    public async Task GroupByAuthor_AuthorFilter_ShowsOnlySelectedAuthorEvenWithCompendiumCoauthors()
    {
        // Repro of the Asimov case: filtering by a single author with grouping
        // by Author should NOT fan out across co-authors of compendiums the
        // selected author contributed to. One filter, one group.
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            var king = new Author { Name = "Stephen King" };
            var bradbury = new Author { Name = "Ray Bradbury" };
            db.Authors.AddRange(asimov, king, bradbury);
            await db.SaveChangesAsync();

            db.Books.AddRange(
                new Book
                {
                    Title = "Foundation",
                    Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }]
                },
                // Compendium with Asimov + co-authors. Pre-fix this would surface
                // King and Bradbury as separate group rows when filtering for Asimov.
                new Book
                {
                    Title = "The Funhouse",
                    Works =
                    [
                        new Work { Title = "Asimov story", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] },
                        new Work { Title = "King story", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] },
                        new Work { Title = "Bradbury story", WorkAuthors = [new WorkAuthor { Author = bradbury, Order = 0 }] },
                    ]
                });
            await db.SaveChangesAsync();
        }

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.Author,
            SelectedAuthor = "Isaac Asimov",
        };
        await vm.InitializeAsync();

        Assert.Single(vm.Groups);
        Assert.Equal("Isaac Asimov", vm.Groups[0].Label);
        Assert.Equal(2, vm.Groups[0].Count); // Foundation + The Funhouse.
    }

    [Fact]
    public async Task GroupByAuthor_AuthorFilterByAlias_ResolvesGroupToCanonical()
    {
        // Filtering by an alias name should produce the canonical's group row,
        // matching the rollup behaviour the unfiltered path asserts.
        var factory = new TestDbContextFactory();
        await SeedSampleLibraryAsync(factory);

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.Author,
            SelectedAuthor = "Richard Bachman",
        };
        await vm.InitializeAsync();

        Assert.Single(vm.Groups);
        Assert.Equal("Stephen King", vm.Groups[0].Label);
        // Only "The Long Walk" matches the alias filter — Carrie's author is
        // King, whose Name is "Stephen King" (not the alias) and whose
        // CanonicalAuthor is null.
        Assert.Equal(1, vm.Groups[0].Count);
    }

    [Fact]
    public async Task GroupByAuthor_BookSearchOnCompendium_GroupsByPrimaryAuthor()
    {
        // Searching for a book title shouldn't fan out the result book's
        // co-authors as separate group rows. Group by the primary
        // (lowest-Order, first-Work) author of each matched book.
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            var king = new Author { Name = "Stephen King" };
            var bradbury = new Author { Name = "Ray Bradbury" };
            db.Authors.AddRange(asimov, king, bradbury);
            await db.SaveChangesAsync();

            db.Books.Add(new Book
            {
                Title = "The Funhouse",
                Works =
                [
                    new Work { Title = "Asimov story", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] },
                    new Work { Title = "King story", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] },
                    new Work { Title = "Bradbury story", WorkAuthors = [new WorkAuthor { Author = bradbury, Order = 0 }] },
                ]
            });
            await db.SaveChangesAsync();
        }

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.Author,
            SearchTerm = "Funhouse",
        };
        await vm.InitializeAsync();

        Assert.Single(vm.Groups);
        // Primary attribution = Asimov (first Work, lowest Order WorkAuthor).
        Assert.Equal("Isaac Asimov", vm.Groups[0].Label);
        Assert.Equal(1, vm.Groups[0].Count);
    }

    [Fact]
    public async Task GroupByAuthor_NoFilter_KeepsFanOutBehaviour()
    {
        // Regression guard: the unfiltered path keeps the post-PR2 fan-out
        // (compendiums + co-authored works appear under each canonical). The
        // narrowed paths above are the only ones that collapse.
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            var king = new Author { Name = "Stephen King" };
            db.Authors.AddRange(asimov, king);
            await db.SaveChangesAsync();

            db.Books.Add(new Book
            {
                Title = "The Funhouse",
                Works =
                [
                    new Work { Title = "Asimov story", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] },
                    new Work { Title = "King story", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] },
                ]
            });
            await db.SaveChangesAsync();
        }

        var vm = new BookListViewModel(TestDispatcher.For(factory)) { SelectedGroupBy = LibraryGroupBy.Author };
        await vm.InitializeAsync();

        // Both authors should have a group row (the deliberate fan-out).
        Assert.Equal(2, vm.Groups.Count);
        Assert.Contains(vm.Groups, g => g.Label == "Isaac Asimov" && g.Count == 1);
        Assert.Contains(vm.Groups, g => g.Label == "Stephen King" && g.Count == 1);
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

        var vm = new BookListViewModel(TestDispatcher.For(factory))
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

    // ---- Inline status / rating quick-set (Library worklist) ----------------

    private static async Task<int> SeedSingleBookAsync(
        TestDbContextFactory factory,
        BookStatus status = BookStatus.Unread,
        int rating = 0,
        string? notes = null)
    {
        using var db = factory.CreateDbContext();
        var author = new Author { Name = "Solo Author" };
        db.Authors.Add(author);
        var book = new Book
        {
            Title = "The Book",
            Status = status,
            Rating = rating,
            Notes = notes,
            Works = [new Work { Title = "The Book", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }],
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    [Fact]
    public async Task StatusFilter_NarrowsFlatList()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var a = new Author { Name = "A" };
            db.Authors.Add(a);
            db.Books.AddRange(
                new Book { Title = "Unread One", Status = BookStatus.Unread, Works = [new Work { Title = "Unread One", WorkAuthors = [new WorkAuthor { Author = a, Order = 0 }] }] },
                new Book { Title = "Read One", Status = BookStatus.Read, Works = [new Work { Title = "Read One", WorkAuthors = [new WorkAuthor { Author = a, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.None,
            SelectedStatus = BookStatus.Unread,
        };
        await vm.InitializeAsync();

        Assert.Single(vm.Books);
        Assert.Equal("Unread One", vm.Books[0].Title);
    }

    [Fact]
    public async Task SetStatusAsync_PersistsAndPatchesLoadedRow()
    {
        var factory = new TestDbContextFactory();
        var bookId = await SeedSingleBookAsync(factory, status: BookStatus.Unread);

        var vm = new BookListViewModel(TestDispatcher.For(factory)) { SelectedGroupBy = LibraryGroupBy.None };
        await vm.InitializeAsync();

        await vm.SetStatusAsync(bookId, BookStatus.Reading);

        using (var db = factory.CreateDbContext())
            Assert.Equal(BookStatus.Reading, db.Books.Single(b => b.Id == bookId).Status);
        // Patched in place — no reload, the row reflects the new status.
        Assert.Equal(BookStatus.Reading, vm.Books.Single(b => b.Id == bookId).Status);
    }

    [Fact]
    public async Task MarkReadAsync_WritesStatusRatingAndNotesTogether()
    {
        // The Mark-Read dialog supplies rating + notes in the same call so
        // nothing is lost to a mid-edit re-filter.
        var factory = new TestDbContextFactory();
        var bookId = await SeedSingleBookAsync(factory, status: BookStatus.Unread);

        var vm = new BookListViewModel(TestDispatcher.For(factory)) { SelectedGroupBy = LibraryGroupBy.None };
        await vm.InitializeAsync();

        await vm.MarkReadAsync(bookId, rating: 4, notes: "Great read");

        using (var db = factory.CreateDbContext())
        {
            var book = db.Books.Single(b => b.Id == bookId);
            Assert.Equal(BookStatus.Read, book.Status);
            Assert.Equal(4, book.Rating);
            Assert.Equal("Great read", book.Notes);
        }
        var row = vm.Books.Single(b => b.Id == bookId);
        Assert.Equal(BookStatus.Read, row.Status);
        Assert.Equal(4, row.Rating);
    }

    [Fact]
    public async Task MarkReadAsync_NullNotes_LeavesExistingNotesIntact()
    {
        // Leaving the dialog's notes field blank passes null — which must
        // preserve any existing notes, not wipe them.
        var factory = new TestDbContextFactory();
        var bookId = await SeedSingleBookAsync(factory, status: BookStatus.Unread, notes: "keep me");

        var vm = new BookListViewModel(TestDispatcher.For(factory)) { SelectedGroupBy = LibraryGroupBy.None };
        await vm.InitializeAsync();

        await vm.MarkReadAsync(bookId, rating: 3, notes: null);

        using var db = factory.CreateDbContext();
        Assert.Equal("keep me", db.Books.Single(b => b.Id == bookId).Notes);
    }

    [Fact]
    public async Task SetStatusAsync_KeepsRowVisibleUnderActiveFilterUntilReload()
    {
        // The core worklist guarantee: marking a book out of the active status
        // filter must NOT make it vanish mid-edit — it stays until the next
        // explicit reload, which is what lets the user finish (e.g. rate it).
        var factory = new TestDbContextFactory();
        var bookId = await SeedSingleBookAsync(factory, status: BookStatus.Unread);

        var vm = new BookListViewModel(TestDispatcher.For(factory))
        {
            SelectedGroupBy = LibraryGroupBy.None,
            SelectedStatus = BookStatus.Unread,
        };
        await vm.InitializeAsync();
        Assert.Single(vm.Books);

        await vm.SetStatusAsync(bookId, BookStatus.Reading);

        // Still present, now showing the new status.
        Assert.Single(vm.Books);
        Assert.Equal(BookStatus.Reading, vm.Books[0].Status);

        // Only an explicit reload re-applies the (Unread) filter and drops it.
        await vm.ApplyFiltersAsync();
        Assert.Empty(vm.Books);
    }

    [Fact]
    public async Task SetRatingAsync_PersistsAndPatchesLoadedRow()
    {
        var factory = new TestDbContextFactory();
        var bookId = await SeedSingleBookAsync(factory, rating: 0);

        var vm = new BookListViewModel(TestDispatcher.For(factory)) { SelectedGroupBy = LibraryGroupBy.None };
        await vm.InitializeAsync();

        await vm.SetRatingAsync(bookId, 5);

        using (var db = factory.CreateDbContext())
            Assert.Equal(5, db.Books.Single(b => b.Id == bookId).Rating);
        Assert.Equal(5, vm.Books.Single(b => b.Id == bookId).Rating);
    }
}
