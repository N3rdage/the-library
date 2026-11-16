using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Series;
using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

// The dialog reads through GetBookEditForm + GetSeriesOptions and writes through
// UpdateBookDetails; a substituted dispatcher stands in for all three. What the
// reads return from SQL is covered by GetBookEditFormHandlerTests and
// GetSeriesOptionsHandlerTests; what the save persists by the UpdateBookDetails
// handler tests.
[Trait("Category", TestCategories.Unit)]
public class BookEditDialogViewModelTests
{
    private const int BookId = 7;
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private void GivenBook(BookEditForm? form) =>
        _dispatcher.Query(Arg.Is<GetBookEditForm>(q => q!.BookId == BookId), Arg.Any<CancellationToken>())
            .Returns(form);

    private void GivenSeries(params SeriesPickerOption[] series) =>
        _dispatcher.Query(Arg.Any<GetSeriesOptions>(), Arg.Any<CancellationToken>())
            .Returns(series);

    private static BookEditForm Book(
        string title = "T", BookCategory category = BookCategory.Fiction, string? cover = null,
        int? seriesId = null, int? seriesOrder = null, string? seriesOrderDisplay = null) =>
        new(BookId, title, category, cover, seriesId, seriesOrder, seriesOrderDisplay);

    private UpdateBookDetails? SentUpdate() =>
        _dispatcher.ReceivedCalls()
            .Select(c => c.GetArguments()[0])
            .OfType<UpdateBookDetails>()
            .SingleOrDefault();

    [Fact]
    public async Task InitializeAsync_MissingId_MarksNotFound()
    {
        GivenBook(null);
        var vm = new BookEditDialogViewModel(_dispatcher);

        await vm.InitializeAsync(BookId);

        Assert.True(vm.NotFound);
    }

    [Fact]
    public async Task InitializeAsync_LoadsCurrentValues()
    {
        GivenBook(Book("Mort", BookCategory.Fiction, "https://example.com/mort.jpg"));
        GivenSeries();
        var vm = new BookEditDialogViewModel(_dispatcher);

        await vm.InitializeAsync(BookId);

        Assert.False(vm.NotFound);
        Assert.Equal(BookId, vm.BookId);
        Assert.Equal("Mort", vm.Title);
        Assert.Equal(BookCategory.Fiction, vm.Category);
        Assert.Equal("https://example.com/mort.jpg", vm.CoverUrl);
        Assert.Null(vm.SelectedSeriesId);
        Assert.Null(vm.SeriesOrderInput);
    }

    [Fact]
    public async Task InitializeAsync_LoadsSeriesOptions()
    {
        GivenBook(Book());
        GivenSeries(
            new SeriesPickerOption(1, "Discworld", SeriesType.Collection),
            new SeriesPickerOption(2, "Witcher", SeriesType.Series));
        var vm = new BookEditDialogViewModel(_dispatcher);

        await vm.InitializeAsync(BookId);

        Assert.Equal(["Discworld", "Witcher"], vm.AvailableSeries.Select(s => s.Name));
        Assert.Equal(SeriesType.Collection, vm.AvailableSeries[0].Type);
    }

    [Fact]
    public async Task InitializeAsync_InSeries_FormatsOrderFromRawSortKeyAndDisplay()
    {
        GivenBook(Book(seriesId: 3, seriesOrder: 4, seriesOrderDisplay: "4.5"));
        GivenSeries();
        var vm = new BookEditDialogViewModel(_dispatcher);

        await vm.InitializeAsync(BookId);

        Assert.Equal(3, vm.SelectedSeriesId);
        Assert.Equal("4.5", vm.SeriesOrderInput);
    }

    [Fact]
    public async Task InitializeAsync_OrderLeftBehindWithoutASeries_IsNotPrefilled()
    {
        // A series delete SET NULLs SeriesId but leaves SeriesOrder behind; the
        // stale order must not pre-fill the field.
        GivenBook(Book(seriesId: null, seriesOrder: 5));
        GivenSeries();
        var vm = new BookEditDialogViewModel(_dispatcher);

        await vm.InitializeAsync(BookId);

        Assert.Null(vm.SeriesOrderInput);
    }

    [Fact]
    public async Task SaveAsync_SendsAllFields()
    {
        GivenBook(Book("Old title"));
        GivenSeries();
        var vm = new BookEditDialogViewModel(_dispatcher);
        await vm.InitializeAsync(BookId);
        vm.Title = "  New title  ";
        vm.Category = BookCategory.NonFiction;
        vm.CoverUrl = "  https://example.com/new.jpg  ";

        await vm.SaveAsync();

        var sent = SentUpdate();
        Assert.NotNull(sent);
        Assert.Equal(BookId, sent.BookId);
        Assert.Equal("  New title  ", sent.Title); // the handler trims
        Assert.Equal(BookCategory.NonFiction, sent.Category);
        Assert.Equal("https://example.com/new.jpg", sent.CoverUrl);
    }

    [Fact]
    public async Task SaveAsync_BlankCoverUrl_SendsNull()
    {
        GivenBook(Book(cover: "https://old.example.com"));
        GivenSeries();
        var vm = new BookEditDialogViewModel(_dispatcher);
        await vm.InitializeAsync(BookId);
        vm.CoverUrl = "   ";

        await vm.SaveAsync();

        var sent = SentUpdate();
        Assert.NotNull(sent);
        Assert.Null(sent.CoverUrl);
    }

    [Fact]
    public async Task SaveAsync_BlankTitle_IsNoOp()
    {
        GivenBook(Book("Untouched"));
        GivenSeries();
        var vm = new BookEditDialogViewModel(_dispatcher);
        await vm.InitializeAsync(BookId);
        vm.Title = "   ";

        await vm.SaveAsync();

        Assert.Null(SentUpdate());
    }

    [Fact]
    public async Task SwitchingSeries_DoesNotCarryStaleOrderIntoNewSeries()
    {
        // Regression (arc-end review): a book in series A at #5, edited and switched
        // to series B without touching the order field, must NOT save as B #5. The
        // dropdown routes through OnSeriesChanged, which resets the order.
        const int seriesA = 1, seriesB = 2;
        GivenBook(Book(seriesId: seriesA, seriesOrder: 5));
        GivenSeries(
            new SeriesPickerOption(seriesB, "Dune", SeriesType.Series),
            new SeriesPickerOption(seriesA, "Witcher", SeriesType.Series));
        var vm = new BookEditDialogViewModel(_dispatcher);
        await vm.InitializeAsync(BookId);
        Assert.Equal("5", vm.SeriesOrderInput);      // loaded order for series A

        vm.OnSeriesChanged(seriesB);                 // user switches the dropdown to Dune
        Assert.Null(vm.SeriesOrderInput);            // stale "5" cleared

        await vm.SaveAsync();

        var sent = SentUpdate();
        Assert.NotNull(sent);
        Assert.Equal(seriesB, sent.SeriesId);
        Assert.Null(sent.SeriesOrder);               // NOT 5 — the old series' order didn't ride over
        Assert.Null(sent.SeriesOrderDisplay);
    }

    [Fact]
    public async Task OnSeriesChanged_SameSeries_KeepsLoadedOrder()
    {
        // Re-selecting the current series (a no-op change) must not wipe the order.
        const int seriesId = 1;
        GivenBook(Book(seriesId: seriesId, seriesOrder: 5));
        GivenSeries(new SeriesPickerOption(seriesId, "Witcher", SeriesType.Series));
        var vm = new BookEditDialogViewModel(_dispatcher);
        await vm.InitializeAsync(BookId);

        vm.OnSeriesChanged(seriesId);                // same series → no reset

        Assert.Equal("5", vm.SeriesOrderInput);
    }
}
