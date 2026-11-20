using BookTracker.Application;
using BookTracker.Application.Series;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace BookTracker.Tests.ViewModels;

// The page contract over a substituted dispatcher: the form and rows come from
// GetSeriesDetail, the add search from SearchBooksForSeries, the added row from
// GetSeriesBookRow, and every write is a sent command; the in-memory Books list
// stays in sync and the friendly duplicate-name error surfaces. The reads' SQL
// (ordering, author display, the take-10 search) is covered by the
// GetSeriesDetail / SearchBooksForSeries / GetSeriesBookRow handler tests, the
// writes' persistence by SeriesCommandHandlersTests.
[Trait("Category", TestCategories.Unit)]
public class SeriesEditViewModelTests
{
    private const int SeriesId = 5;
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private SeriesEditViewModel NewVm() => new(_dispatcher);

    private static SeriesBookRow Row(int id, string title, int? order = null, string? display = null, string author = "a") =>
        new(id, title, author, order, display);

    private void GivenSeries(params SeriesBookRow[] books) =>
        _dispatcher.Query(Arg.Is<GetSeriesDetail>(q => q!.SeriesId == SeriesId), Arg.Any<CancellationToken>())
            .Returns(new SeriesDetail(SeriesId, "Foundation", "Isaac Asimov", SeriesType.Series, 7, "desc", books));

    private T[] Sent<T>() =>
        _dispatcher.ReceivedCalls()
            .Select(c => c.GetArguments()[0])
            .OfType<T>()
            .ToArray();

    // --- Initialize ----------------------------------------------------------

    [Fact]
    public void InitializeNew_setsIsNew_andEmptyInput()
    {
        var vm = NewVm();
        vm.InitializeNew();

        Assert.True(vm.IsNew);
        Assert.NotNull(vm.Input);
        Assert.Null(vm.Input!.Name);
    }

    [Fact]
    public async Task InitializeAsync_missing_marksNotFound()
    {
        _dispatcher.Query(Arg.Any<GetSeriesDetail>(), Arg.Any<CancellationToken>()).Returns((SeriesDetail?)null);

        var vm = NewVm();
        await vm.InitializeAsync(999999);

        Assert.True(vm.NotFound);
    }

    [Fact]
    public async Task InitializeAsync_loadsDetails_andBooksInQueryOrder()
    {
        GivenSeries(Row(1, "Foundation", 1), Row(2, "Second Foundation", 2));

        var vm = NewVm();
        await vm.InitializeAsync(SeriesId);

        Assert.False(vm.NotFound);
        Assert.False(vm.IsNew);
        Assert.Equal("Foundation", vm.Input!.Name);
        Assert.Equal("Isaac Asimov", vm.Input.Author);
        Assert.Equal(SeriesType.Series, vm.Input.Type);
        Assert.Equal(7, vm.Input.ExpectedCount);
        Assert.Equal("desc", vm.Input.Description);
        Assert.Equal(["Foundation", "Second Foundation"], vm.Books.Select(b => b.Title));
    }

    // --- SaveAsync (create) --------------------------------------------------

    [Fact]
    public async Task SaveAsync_create_sendsRawValues_andReturnsId()
    {
        _dispatcher.Send(Arg.Any<CreateSeries>(), Arg.Any<CancellationToken>()).Returns(42);
        var vm = NewVm();
        vm.InitializeNew();
        vm.Input!.Name = "  Foundation  ";
        vm.Input.Author = "Isaac Asimov";
        vm.Input.Type = SeriesType.Series;
        vm.Input.ExpectedCount = 7;

        var id = await vm.SaveAsync(null);

        Assert.Equal(42, id);
        var sent = Assert.Single(Sent<CreateSeries>());
        Assert.Equal(new CreateSeries("  Foundation  ", "Isaac Asimov", SeriesType.Series, 7, null), sent); // aggregate trims
    }

    [Fact]
    public async Task SaveAsync_create_duplicateName_setsError_returnsNull()
    {
        _dispatcher.Send(Arg.Any<CreateSeries>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("A series named \"Discworld\" already exists."));
        var vm = NewVm();
        vm.InitializeNew();
        vm.Input!.Name = "discworld";

        var id = await vm.SaveAsync(null);

        Assert.Null(id);
        Assert.False(string.IsNullOrEmpty(vm.Error));
        Assert.False(vm.Saving);
    }

    // --- SaveAsync (edit) ----------------------------------------------------

    [Fact]
    public async Task SaveAsync_edit_sendsUpdate_andSetsSuccess()
    {
        GivenSeries();
        var vm = NewVm();
        await vm.InitializeAsync(SeriesId);
        vm.Input!.Name = "New Name";
        vm.Input.Author = "New Author";

        var id = await vm.SaveAsync(SeriesId);

        Assert.Equal(SeriesId, id);
        Assert.False(string.IsNullOrEmpty(vm.SuccessMessage));
        var sent = Assert.Single(Sent<UpdateSeries>());
        Assert.Equal(new UpdateSeries(SeriesId, "New Name", "New Author", SeriesType.Series, 7, "desc"), sent);
    }

    [Fact]
    public async Task SaveAsync_edit_missing_showsMessage_returnsNull()
    {
        GivenSeries();
        _dispatcher.Send(Arg.Any<UpdateSeries>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotFoundException("gone"));
        var vm = NewVm();
        await vm.InitializeAsync(SeriesId);
        vm.Input!.Name = "Renamed";

        var id = await vm.SaveAsync(SeriesId);

        Assert.Null(id);
        Assert.Equal("gone", vm.Error);
    }

    [Fact]
    public async Task SaveAsync_failedSaveAfterSuccess_clearsStaleSuccessMessage()
    {
        GivenSeries();
        var vm = NewVm();
        await vm.InitializeAsync(SeriesId);
        vm.Input!.Name = "Keeper Renamed";
        await vm.SaveAsync(SeriesId);                          // success → SuccessMessage set
        Assert.False(string.IsNullOrEmpty(vm.SuccessMessage));

        _dispatcher.Send(Arg.Any<UpdateSeries>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("taken"));
        vm.Input.Name = "Taken";
        var id = await vm.SaveAsync(SeriesId);

        Assert.Null(id);
        Assert.False(string.IsNullOrEmpty(vm.Error));
        Assert.True(string.IsNullOrEmpty(vm.SuccessMessage)); // no dual green+red banner
    }

    // --- Delete --------------------------------------------------------------

    [Fact]
    public async Task DeleteSeriesAsync_sendsDelete_andReturnsTrue()
    {
        var vm = NewVm();
        var ok = await vm.DeleteSeriesAsync(SeriesId);

        Assert.True(ok);
        Assert.False(vm.Deleting);
        Assert.Equal(new DeleteSeries(SeriesId), Assert.Single(Sent<DeleteSeries>()));
    }

    // --- Book membership -----------------------------------------------------

    [Fact]
    public async Task AddBookToSeriesAsync_sendsAdd_andAppendsTheRowReadBack()
    {
        GivenSeries(Row(1, "Colour of Magic", 1));
        _dispatcher.Query(Arg.Is<GetSeriesBookRow>(q => q!.BookId == 2), Arg.Any<CancellationToken>())
            .Returns(Row(2, "The Light Fantastic", 2));
        var vm = NewVm();
        await vm.InitializeAsync(SeriesId);

        await vm.AddBookToSeriesAsync(SeriesId, 2);

        Assert.Equal(new AddBookToSeries(SeriesId, 2), Assert.Single(Sent<AddBookToSeries>()));
        Assert.Equal(2, vm.Books.Count);
        Assert.Equal(2, vm.Books.Single(b => b.Id == 2).SeriesOrder); // the order the handler set
    }

    [Fact]
    public async Task AddBookToSeriesAsync_bookGone_addsNoRow()
    {
        GivenSeries();
        _dispatcher.Query(Arg.Any<GetSeriesBookRow>(), Arg.Any<CancellationToken>()).Returns((SeriesBookRow?)null);
        var vm = NewVm();
        await vm.InitializeAsync(SeriesId);

        await vm.AddBookToSeriesAsync(SeriesId, 3);

        Assert.Empty(vm.Books);
    }

    [Fact]
    public async Task AddBookToSeriesAsync_calledTwiceForSameBook_addsOneRow()
    {
        GivenSeries();
        _dispatcher.Query(Arg.Is<GetSeriesBookRow>(q => q!.BookId == 4), Arg.Any<CancellationToken>())
            .Returns(Row(4, "Sourcery", 1));
        var vm = NewVm();
        await vm.InitializeAsync(SeriesId);

        await vm.AddBookToSeriesAsync(SeriesId, 4);
        await vm.AddBookToSeriesAsync(SeriesId, 4);  // double-fire (e.g. double-click)

        Assert.Single(vm.Books);
        Assert.Single(Sent<AddBookToSeries>());
    }

    [Fact]
    public async Task RemoveBookFromSeriesAsync_sendsRemove_andDropsRow()
    {
        GivenSeries(Row(1, "Edgedancer", 4, "4.5"));
        var vm = NewVm();
        await vm.InitializeAsync(SeriesId);

        await vm.RemoveBookFromSeriesAsync(1);

        Assert.Empty(vm.Books);
        Assert.Equal(new RemoveBookFromSeries(1), Assert.Single(Sent<RemoveBookFromSeries>()));
    }

    [Fact]
    public async Task UpdateBookOrderAsync_parsesSends_andUpdatesRow()
    {
        GivenSeries(Row(1, "Words of Radiance", 2));
        var vm = NewVm();
        await vm.InitializeAsync(SeriesId);

        await vm.UpdateBookOrderAsync(1, "4.5"); // interquel

        var row = vm.Books.Single(b => b.Id == 1);
        Assert.Equal(4, row.SeriesOrder);
        Assert.Equal("4.5", row.SeriesOrderDisplay);
        Assert.Equal(new SetBookSeriesOrder(1, 4, "4.5"), Assert.Single(Sent<SetBookSeriesOrder>()));
    }

    // --- Book search ---------------------------------------------------------

    [Fact]
    public async Task SearchBooksAsync_blankTerm_clearsResults_withoutQuerying()
    {
        var vm = NewVm();
        vm.BookSearchTerm = "   ";

        await vm.SearchBooksAsync();

        Assert.Empty(vm.BookSearchResults);
        Assert.Empty(_dispatcher.ReceivedCalls());
    }

    [Fact]
    public async Task SearchBooksAsync_sendsTrimmedTerm_excludingBooksAlreadyShown()
    {
        GivenSeries(Row(1, "Relic I", 1), Row(2, "Relic II", 2));
        SearchBooksForSeries? sent = null;
        _dispatcher.Query(Arg.Do<SearchBooksForSeries>(q => sent = q), Arg.Any<CancellationToken>())
            .Returns([new SeriesBookSearchResult(9, "Relic", "Douglas Preston, Lincoln Child", null)]);
        var vm = NewVm();
        await vm.InitializeAsync(SeriesId);
        vm.BookSearchTerm = "  Relic ";

        await vm.SearchBooksAsync();

        Assert.NotNull(sent);
        Assert.Equal("Relic", sent.Term);
        Assert.Equal([1, 2], sent.ExcludeIds.Order());
        var result = Assert.Single(vm.BookSearchResults);
        Assert.Equal("Douglas Preston, Lincoln Child", result.Author);
    }

    [Fact]
    public async Task AddBookToSeriesAsync_removesTheBookFromSearchResults()
    {
        GivenSeries();
        _dispatcher.Query(Arg.Any<SearchBooksForSeries>(), Arg.Any<CancellationToken>())
            .Returns([new SeriesBookSearchResult(9, "Relic", "x", null), new SeriesBookSearchResult(10, "Reliquary", "x", null)]);
        _dispatcher.Query(Arg.Is<GetSeriesBookRow>(q => q!.BookId == 9), Arg.Any<CancellationToken>())
            .Returns(Row(9, "Relic", 1, author: "x"));
        var vm = NewVm();
        await vm.InitializeAsync(SeriesId);
        vm.BookSearchTerm = "Rel";
        await vm.SearchBooksAsync();

        await vm.AddBookToSeriesAsync(SeriesId, 9);

        Assert.Equal([10], vm.BookSearchResults.Select(r => r.Id));
    }
}
