using BookTracker.Application;
using BookTracker.Application.Series;
using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

// Thin unit tests for the VM-side of /series: the completion badge/text
// presentation helpers and the SelectedType string -> SeriesType? parse that
// feeds GetSeriesList. The filtering + projection are covered by
// GetSeriesListHandlerTests.
[Trait("Category", TestCategories.Unit)]
public class SeriesListViewModelTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private static SeriesListItem Item(SeriesType type, int workCount, int? expected) =>
        new(1, "S", "A", type, workCount, expected);

    [Fact]
    public void CompletionText_SeriesWithExpected_ShowsProgress()
    {
        Assert.Equal("3 / 7", SeriesListViewModel.CompletionText(Item(SeriesType.Series, 3, 7)));
    }

    [Fact]
    public void CompletionText_NoExpectedOrCollection_ShowsWorkCount()
    {
        Assert.Equal("3 works", SeriesListViewModel.CompletionText(Item(SeriesType.Series, 3, null)));
        Assert.Equal("5 works", SeriesListViewModel.CompletionText(Item(SeriesType.Collection, 5, 9)));
    }

    [Fact]
    public void CompletionBadgeClass_CompleteVsIncomplete()
    {
        Assert.Equal("bg-success", SeriesListViewModel.CompletionBadgeClass(Item(SeriesType.Series, 7, 7)));
        Assert.Equal("bg-warning text-dark", SeriesListViewModel.CompletionBadgeClass(Item(SeriesType.Series, 3, 7)));
        Assert.Equal("bg-light text-dark border", SeriesListViewModel.CompletionBadgeClass(Item(SeriesType.Collection, 5, 9)));
    }

    [Fact]
    public async Task LoadSeriesAsync_ParsesSelectedType_AndPopulatesFromQuery()
    {
        _dispatcher.Query(Arg.Any<GetSeriesList>(), Arg.Any<CancellationToken>())
            .Returns([Item(SeriesType.Series, 1, 3)]);

        var vm = new SeriesListViewModel(_dispatcher) { SearchTerm = "foo", SelectedType = "Collection" };
        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.Single(vm.AllSeries);
        await _dispatcher.Received().Query(
            Arg.Is<GetSeriesList>(q => q.Search == "foo" && q.Type == SeriesType.Collection),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadSeriesAsync_EmptySelectedType_PassesNullType()
    {
        _dispatcher.Query(Arg.Any<GetSeriesList>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var vm = new SeriesListViewModel(_dispatcher);
        await vm.InitializeAsync();

        await _dispatcher.Received().Query(
            Arg.Is<GetSeriesList>(q => q.Type == null),
            Arg.Any<CancellationToken>());
    }
}
