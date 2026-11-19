using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Publishers;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

// The dialog reads through GetEditionForEdit + GetPublisherOptions and writes
// through AddEditionToBook / UpdateEdition; a substituted dispatcher stands in
// for all four. The reads' SQL is covered by GetEditionForEditHandlerTests and
// GetPublisherOptionsHandlerTests, the writes' persistence (publisher
// find-or-create, blank ISBN, first copy) by BookCommandHandlersTests.
[Trait("Category", TestCategories.Unit)]
public class EditionFormDialogViewModelTests
{
    private const int EditionId = 3;
    private const int BookId = 42;
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    public EditionFormDialogViewModelTests()
    {
        GivenPublishers();
    }

    private EditionFormDialogViewModel NewVm() => new(_lookup, _dispatcher);

    private void GivenPublishers(params PublisherOption[] publishers) =>
        _dispatcher.Query(Arg.Any<GetPublisherOptions>(), Arg.Any<CancellationToken>())
            .Returns(publishers);

    private void GivenEdition(EditionForEdit? edition) =>
        _dispatcher.Query(Arg.Is<GetEditionForEdit>(q => q!.EditionId == EditionId), Arg.Any<CancellationToken>())
            .Returns(edition);

    [Fact]
    public async Task InitializeForAdd_SetsIsNewAndBookId()
    {
        var vm = NewVm();

        await vm.InitializeForAddAsync(BookId);

        Assert.True(vm.IsNew);
        Assert.Equal(BookId, vm.BookId);
        Assert.Null(vm.EditionId);
    }

    [Fact]
    public async Task InitializeForEditAsync_MissingId_MarksNotFound()
    {
        GivenEdition(null);
        var vm = NewVm();

        await vm.InitializeForEditAsync(EditionId);

        Assert.True(vm.NotFound);
    }

    [Fact]
    public async Task InitializeForEditAsync_LoadsEditionFields()
    {
        GivenEdition(new EditionForEdit(
            EditionId, BookId, "9780552131063", BookFormat.MassMarketPaperback, "Corgi",
            new DateOnly(1987, 1, 1), DatePrecision.Year, "https://example.com/mort.jpg"));
        var vm = NewVm();

        await vm.InitializeForEditAsync(EditionId);

        Assert.False(vm.IsNew);
        Assert.False(vm.NotFound);
        Assert.Equal(BookId, vm.BookId);
        Assert.Equal("9780552131063", vm.Isbn);
        Assert.Equal(BookFormat.MassMarketPaperback, vm.Format);
        Assert.Equal("Corgi", vm.Publisher);
        Assert.Equal("1987", vm.FirstPublishedOrPrintedDate);
        Assert.Equal("https://example.com/mort.jpg", vm.CoverUrl);
    }

    [Fact]
    public async Task InitializeForEditAsync_NoPublisher_LeavesPublisherNull()
    {
        GivenEdition(new EditionForEdit(
            EditionId, BookId, null, BookFormat.Hardcover, null, null, DatePrecision.Day, null));
        var vm = NewVm();

        await vm.InitializeForEditAsync(EditionId);

        Assert.Null(vm.Publisher);
        Assert.Equal("", vm.FirstPublishedOrPrintedDate);
    }

    [Fact]
    public async Task InitializeForEditAsync_CachesExistingPublishers()
    {
        GivenEdition(new EditionForEdit(
            EditionId, BookId, null, BookFormat.Hardcover, null, null, DatePrecision.Day, null));
        GivenPublishers(new PublisherOption(1, "Corgi"), new PublisherOption(2, "Orbit"));
        var vm = NewVm();

        await vm.InitializeForEditAsync(EditionId);

        Assert.Equal(["Corgi", "Orbit"], vm.ExistingPublishers.Select(p => p.Name));
    }

    [Fact]
    public async Task SaveAsync_Add_SendsAddEditionToBookWithFirstCopy()
    {
        _dispatcher.Send(Arg.Any<AddEditionToBook>(), Arg.Any<CancellationToken>()).Returns(77);
        var vm = NewVm();
        await vm.InitializeForAddAsync(BookId);
        vm.Isbn = "9780552131063";
        vm.Format = BookFormat.MassMarketPaperback;
        vm.Publisher = "Corgi";
        vm.FirstPublishedOrPrintedDate = "1987";
        vm.FirstCopyCondition = BookCondition.VeryGood;

        var id = await vm.SaveAsync();

        Assert.Equal(77, id);
        await _dispatcher.Received(1).Send(
            Arg.Is<AddEditionToBook>(c => c!.BookId == BookId
                && c.Isbn == "9780552131063"
                && c.Format == BookFormat.MassMarketPaperback
                && c.PublisherName == "Corgi"
                && c.DatePrinted == new DateOnly(1987, 1, 1)
                && c.DatePrintedPrecision == DatePrecision.Year
                && c.FirstCopyCondition == BookCondition.VeryGood),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_Edit_SendsUpdateEdition_AndReturnsEditionId()
    {
        GivenEdition(new EditionForEdit(
            EditionId, BookId, "old", BookFormat.Hardcover, null, null, DatePrecision.Day, null));
        var vm = NewVm();
        await vm.InitializeForEditAsync(EditionId);
        vm.Isbn = "9780000000001";
        vm.Format = BookFormat.TradePaperback;
        vm.Publisher = "NewPub";

        var id = await vm.SaveAsync();

        Assert.Equal(EditionId, id);
        await _dispatcher.Received(1).Send(
            Arg.Is<UpdateEdition>(c => c!.EditionId == EditionId
                && c.Isbn == "9780000000001"
                && c.Format == BookFormat.TradePaperback
                && c.PublisherName == "NewPub"),
            Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().Send(Arg.Any<AddEditionToBook>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeForAdd_CachesExistingPublishersInOrder()
    {
        // The publisher autocomplete + eager-create commit (TD-15a) work off this
        // client-side cache: filtered locally for suggestions, and consulted to
        // tell an existing pick from a genuinely new name.
        GivenPublishers(new PublisherOption(1, "Corgi"), new PublisherOption(2, "Orbit"));
        var vm = NewVm();

        await vm.InitializeForAddAsync(BookId);

        Assert.Equal(["Corgi", "Orbit"], vm.ExistingPublishers.Select(p => p.Name));
    }

    [Fact]
    public async Task ExistingPublishers_IsAMutableCopy_SoANewPublisherCanBeAppended()
    {
        var fromQuery = new[] { new PublisherOption(1, "Corgi") };
        GivenPublishers(fromQuery);
        var vm = NewVm();
        await vm.InitializeForAddAsync(BookId);

        vm.ExistingPublishers.Add(new PublisherOption(2, "Gollancz"));

        Assert.Equal(2, vm.ExistingPublishers.Count);
        Assert.Single(fromQuery);
    }

    [Fact]
    public async Task SearchPublishersAsync_MatchesSubstring()
    {
        GivenPublishers(
            new PublisherOption(1, "Corgi"),
            new PublisherOption(2, "Gollancz"),
            new PublisherOption(3, "Orbit"));
        var vm = NewVm();
        await vm.InitializeForAddAsync(BookId);

        var results = (await vm.SearchPublishersAsync("or", CancellationToken.None)).ToList();

        Assert.Contains("Corgi", results);
        Assert.Contains("Orbit", results);
        Assert.DoesNotContain("Gollancz", results);
    }

    [Fact]
    public async Task LookupAsync_PopulatesEmptyFieldsFromResult()
    {
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780552131063",
                Title: "Mort",
                Subtitle: null,
                Author: "Pratchett",
                Publisher: "Corgi",
                GenreCandidates: [],
                DatePrinted: new DateOnly(1987, 11, 12),
                CoverUrl: "https://example.com/mort.jpg",
                Source: "OpenLibrary",
                Format: BookFormat.MassMarketPaperback,
                DatePrintedPrecision: DatePrecision.Day));

        var vm = NewVm();
        await vm.InitializeForAddAsync(1);
        vm.Isbn = "9780552131063";
        await vm.LookupAsync();

        Assert.Equal("Corgi", vm.Publisher);
        Assert.Equal("https://example.com/mort.jpg", vm.CoverUrl);
        Assert.Equal(BookFormat.MassMarketPaperback, vm.Format);
        Assert.NotEmpty(vm.FirstPublishedOrPrintedDate);
    }
}
