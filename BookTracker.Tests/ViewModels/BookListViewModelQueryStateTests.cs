using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

// Pure unit tests for the URL-backed filter state (PR1). ToQueryParameters /
// ApplyQueryParameters never touch the DbContext, so the VM is constructed with
// a null factory — these stay container-free and fast.
[Trait("Category", TestCategories.Unit)]
public class BookListViewModelQueryStateTests
{
    private static BookListViewModel NewVm() => new(null!);

    [Fact]
    public void ToQueryParameters_OmitsDefaults()
    {
        var vm = NewVm(); // all defaults: Author grouping, page 1, no filters.

        var q = vm.ToQueryParameters();

        // Defaults are omitted entirely so the URL stays clean (/books).
        Assert.Null(q["q"]);
        Assert.Null(q["group"]);   // Author is the default grouping
        Assert.Null(q["category"]);
        Assert.Null(q["genre"]);
        Assert.Null(q["tag"]);
        Assert.Null(q["status"]);
        Assert.Null(q["author"]);
        Assert.Null(q["page"]);    // page 1 is the default
    }

    [Fact]
    public void ToQueryParameters_EmitsSetFilters()
    {
        var vm = NewVm();
        vm.SearchTerm = "  dune  ";          // trimmed on serialise
        vm.SelectedGroupBy = LibraryGroupBy.Genre;
        vm.SelectedCategory = "Fiction";
        vm.SelectedGenreId = 7;
        vm.SelectedTagId = 3;
        vm.SelectedStatus = BookStatus.Unread;
        vm.SelectedAuthor = " Herbert ";
        vm.CurrentPage = 4;

        var q = vm.ToQueryParameters();

        Assert.Equal("dune", q["q"]);
        Assert.Equal("Genre", q["group"]);
        Assert.Equal("Fiction", q["category"]);
        Assert.Equal(7, q["genre"]);
        Assert.Equal(3, q["tag"]);
        Assert.Equal("Unread", q["status"]);
        Assert.Equal("Herbert", q["author"]);
        Assert.Equal(4, q["page"]);
    }

    [Fact]
    public void ApplyQueryParameters_HydratesFields()
    {
        var vm = NewVm();

        vm.ApplyQueryParameters(
            q: "dune", group: "Collection", category: "NonFiction",
            genre: 7, tag: 3, status: "Reading", author: "Herbert", page: 4);

        Assert.Equal("dune", vm.SearchTerm);
        Assert.Equal(LibraryGroupBy.Collection, vm.SelectedGroupBy);
        Assert.Equal("NonFiction", vm.SelectedCategory);
        Assert.Equal(7, vm.SelectedGenreId);
        Assert.Equal(3, vm.SelectedTagId);
        Assert.Equal(BookStatus.Reading, vm.SelectedStatus);
        Assert.Equal("Herbert", vm.SelectedAuthor);
        Assert.Equal(4, vm.CurrentPage);
    }

    [Fact]
    public void ApplyQueryParameters_FallsBackToDefaultsForAbsentOrJunk()
    {
        var vm = NewVm();

        // Absent params + an unparseable status/group degrade to defaults
        // rather than throwing — round-tripping an omitted value is lossless.
        vm.ApplyQueryParameters(
            q: null, group: "NotAGroup", category: null,
            genre: null, tag: null, status: "Bogus", author: null, page: 0);

        Assert.Equal("", vm.SearchTerm);
        Assert.Equal(LibraryGroupBy.Author, vm.SelectedGroupBy);
        Assert.Equal("", vm.SelectedCategory);
        Assert.Equal(0, vm.SelectedGenreId);
        Assert.Equal(0, vm.SelectedTagId);
        Assert.Null(vm.SelectedStatus);
        Assert.Equal("", vm.SelectedAuthor);
        Assert.Equal(1, vm.CurrentPage);
    }

    [Theory]
    [InlineData("plague", LibraryGroupBy.None, "Fiction", 12, 5, BookStatus.Read, "Camus", 2)]
    [InlineData("", LibraryGroupBy.Author, "", 0, 0, null, "", 1)]
    [InlineData("x", LibraryGroupBy.Collection, "NonFiction", 0, 9, BookStatus.Reference, "", 3)]
    public void RoundTrip_SerialiseThenHydrate_PreservesState(
        string term, LibraryGroupBy group, string category,
        int genreId, int tagId, BookStatus? status, string author, int page)
    {
        var source = NewVm();
        source.SearchTerm = term;
        source.SelectedGroupBy = group;
        source.SelectedCategory = category;
        source.SelectedGenreId = genreId;
        source.SelectedTagId = tagId;
        source.SelectedStatus = status;
        source.SelectedAuthor = author;
        source.CurrentPage = page;

        var q = source.ToQueryParameters();

        // Feed the serialised values back through the parser, mimicking what the
        // page does after a NavigateTo round-trips through the query string.
        var rehydrated = NewVm();
        rehydrated.ApplyQueryParameters(
            q: (string?)q["q"],
            group: (string?)q["group"],
            category: (string?)q["category"],
            genre: (int?)q["genre"],
            tag: (int?)q["tag"],
            status: (string?)q["status"],
            author: (string?)q["author"],
            page: (int?)q["page"]);

        Assert.Equal(term, rehydrated.SearchTerm);
        Assert.Equal(group, rehydrated.SelectedGroupBy);
        Assert.Equal(category, rehydrated.SelectedCategory);
        Assert.Equal(genreId, rehydrated.SelectedGenreId);
        Assert.Equal(tagId, rehydrated.SelectedTagId);
        Assert.Equal(status, rehydrated.SelectedStatus);
        Assert.Equal(author, rehydrated.SelectedAuthor);
        Assert.Equal(page, rehydrated.CurrentPage);
    }
}
