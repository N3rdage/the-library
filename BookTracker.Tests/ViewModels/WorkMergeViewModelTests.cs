using BookTracker.Application;
using BookTracker.Application.Works;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Unit)]
public class WorkMergeViewModelTests
{
    private readonly IWorkMergeService _merger = Substitute.For<IWorkMergeService>();
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private WorkMergeViewModel CreateVm() => new(_merger, _dispatcher);

    private static WorkMergeDetail Detail(int id, string title) =>
        new(id, title, null, "A", null, null, null, [], 0, [], null);

    [Fact]
    public async Task LoadAsync_populates_details_and_shared_book_count()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new WorkMergeLoadResult(Detail(1, "A"), Detail(2, "B"), null, SharedBookCount: 3));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        Assert.False(vm.Loading);
        Assert.Equal(3, vm.SharedBookCount);
        Assert.Null(vm.IncompatibilityReason);
    }

    [Fact]
    public async Task LoadAsync_surfaces_incompatibility_reason()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new WorkMergeLoadResult(Detail(1, "A"), Detail(2, "B"), "different authors", 0));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        Assert.Equal("different authors", vm.IncompatibilityReason);
        Assert.False(vm.CanMerge);
    }

    [Fact]
    public async Task MergeAsync_dispatches_merge_command_and_reports_success()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new WorkMergeLoadResult(Detail(1, "A"), Detail(2, "B"), null, 0));
        _dispatcher.Send(Arg.Any<MergeWorks>(), Arg.Any<CancellationToken>())
            .Returns(new WorkMergeResult(true, null, 3, 0, 0, "A", "B"));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        var result = await vm.MergeAsync();

        Assert.NotNull(result);
        Assert.True(result!.Success);
        await _dispatcher.Received(1).Send(
            Arg.Is<MergeWorks>(c => c.WinnerId == 1 && c.LoserId == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergeAsync_surfaces_error_on_failure()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new WorkMergeLoadResult(Detail(1, "A"), Detail(2, "B"), null, 0));
        _dispatcher.Send(Arg.Any<MergeWorks>(), Arg.Any<CancellationToken>())
            .Returns(new WorkMergeResult(false, "boom", 0, 0, 0, null, null));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        await vm.MergeAsync();

        Assert.Equal("boom", vm.ErrorMessage);
    }

    [Fact]
    public async Task MergeAsync_noop_without_winner_selection()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new WorkMergeLoadResult(Detail(1, "A"), Detail(2, "B"), null, 0));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        var result = await vm.MergeAsync();

        Assert.Null(result);
        await _dispatcher.DidNotReceiveWithAnyArgs().Send(Arg.Any<MergeWorks>(), Arg.Any<CancellationToken>());
    }
}
