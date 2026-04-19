using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

public class BookAddViewModelTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();

    private BookAddViewModel CreateVm() =>
        new(_factory, _lookup, new SeriesMatchService(_factory));

    private GenrePickerViewModel CreateGenrePicker() => new(_factory);

    [Fact]
    public async Task SearchAsync_EmptyInputs_ReportsMessageAndDoesNotCallLookup()
    {
        var vm = CreateVm();
        vm.SearchTitle = "";
        vm.SearchAuthor = "  ";

        await vm.SearchAsync();

        Assert.NotNull(vm.SearchMessage);
        await _lookup.DidNotReceiveWithAnyArgs().SearchByTitleAuthorAsync(default, default, default);
    }

    [Fact]
    public async Task SearchAsync_PopulatesCandidates()
    {
        var vm = CreateVm();
        vm.SearchTitle = "Murder on the Orient Express";
        vm.SearchAuthor = "Agatha Christie";

        var candidate = new BookSearchCandidate(
            WorkKey: "/works/OL45804W",
            Title: "Murder on the Orient Express",
            Author: "Agatha Christie",
            FirstPublishYear: 1934,
            EditionCount: 245,
            CoverUrl: "https://covers.openlibrary.org/b/id/12345-M.jpg",
            OpenLibraryUrl: "https://openlibrary.org/works/OL45804W/editions");

        _lookup.SearchByTitleAuthorAsync("Murder on the Orient Express", "Agatha Christie", Arg.Any<CancellationToken>())
            .Returns([candidate]);

        await vm.SearchAsync();

        Assert.Single(vm.SearchCandidates);
        Assert.Equal("Murder on the Orient Express", vm.SearchCandidates[0].Title);
        Assert.Null(vm.SearchMessage);
    }

    [Fact]
    public async Task SearchAsync_NoResults_SetsHelpfulMessage()
    {
        var vm = CreateVm();
        vm.SearchTitle = "An obscure title that doesn't exist";

        _lookup.SearchByTitleAuthorAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BookSearchCandidate>());

        await vm.SearchAsync();

        Assert.Empty(vm.SearchCandidates);
        Assert.Contains("No matches", vm.SearchMessage);
    }

    [Fact]
    public async Task ApplyCandidateAsync_PrefillsEmptyFields()
    {
        var vm = CreateVm();
        var candidate = new BookSearchCandidate(
            WorkKey: "/works/OL1W",
            Title: "And Then There Were None",
            Author: "Agatha Christie",
            FirstPublishYear: 1939,
            EditionCount: 100,
            CoverUrl: "https://example.invalid/cover.jpg",
            OpenLibraryUrl: null);

        await vm.ApplyCandidateAsync(candidate, CreateGenrePicker());

        Assert.Equal("And Then There Were None", vm.BookInput.Title);
        Assert.Equal("Agatha Christie", vm.BookInput.Author);
        Assert.Equal("https://example.invalid/cover.jpg", vm.BookInput.DefaultCoverArtUrl);
        Assert.Equal(new DateOnly(1939, 1, 1), vm.EditionInput.DatePrinted);
    }

    [Fact]
    public async Task ApplyCandidateAsync_DoesNotOverwriteUserFilledFields()
    {
        var vm = CreateVm();
        vm.BookInput.Title = "User typed this";
        vm.BookInput.Author = "User author";
        vm.EditionInput.DatePrinted = new DateOnly(1972, 6, 15);

        var candidate = new BookSearchCandidate(
            WorkKey: "/works/OL1W",
            Title: "Lookup title",
            Author: "Lookup author",
            FirstPublishYear: 1939,
            EditionCount: 1,
            CoverUrl: null,
            OpenLibraryUrl: null);

        await vm.ApplyCandidateAsync(candidate, CreateGenrePicker());

        Assert.Equal("User typed this", vm.BookInput.Title);
        Assert.Equal("User author", vm.BookInput.Author);
        Assert.Equal(new DateOnly(1972, 6, 15), vm.EditionInput.DatePrinted);
    }

    [Fact]
    public async Task SaveAsync_BlankIsbnPersistsAsNull()
    {
        var vm = CreateVm();
        vm.BookInput.Title = "And Then There Were None";
        vm.BookInput.Author = "Agatha Christie";
        vm.EditionInput.Isbn = "";

        var ok = await vm.SaveAsync(new List<int>());

        Assert.True(ok);
        using var db = _factory.CreateDbContext();
        var edition = db.Editions.Single();
        Assert.Null(edition.Isbn);
    }

    [Fact]
    public async Task SaveAsync_TwoNoIsbnEditions_CoexistWithoutCollision()
    {
        // Verifies the filtered unique index actually permits multiple null
        // ISBNs. With an unfiltered unique index this would throw on the
        // second save.
        var vm1 = CreateVm();
        vm1.BookInput.Title = "Book A";
        vm1.BookInput.Author = "Author A";
        Assert.True(await vm1.SaveAsync(new List<int>()));

        var vm2 = CreateVm();
        vm2.BookInput.Title = "Book B";
        vm2.BookInput.Author = "Author B";
        Assert.True(await vm2.SaveAsync(new List<int>()));

        using var db = _factory.CreateDbContext();
        Assert.Equal(2, db.Editions.Count(e => e.Isbn == null));
    }
}
