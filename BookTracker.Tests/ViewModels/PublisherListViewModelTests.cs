using BookTracker.Application;
using BookTracker.Application.Publishers;
using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

// Thin unit tests for the VM-side of /publishers: the expand/lazy-detail cache
// and the toast + cache-invalidation wiring around the write commands. The data
// reads and write effects are covered by GetPublisherListHandlerTests /
// PublisherAdminCommandsTests.
[Trait("Category", TestCategories.Unit)]
public class PublisherListViewModelTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private void StubList(params PublisherRow[] rows) =>
        _dispatcher.Query(Arg.Any<GetPublisherList>(), Arg.Any<CancellationToken>()).Returns(rows);

    private async Task<PublisherListViewModel> LoadedVm(params PublisherRow[] rows)
    {
        StubList(rows);
        var vm = new PublisherListViewModel(_dispatcher);
        await vm.LoadAsync();
        return vm;
    }

    [Fact]
    public async Task LoadAsync_PopulatesPublishers()
    {
        var vm = await LoadedVm(new PublisherRow(1, "Tor", 2), new PublisherRow(2, "Unused", 0));

        Assert.False(vm.Loading);
        Assert.Equal(2, vm.Publishers.Count);
    }

    [Fact]
    public async Task ToggleExpandAsync_LoadsDetailOnce_ThenCollapses()
    {
        var vm = await LoadedVm(new PublisherRow(1, "Tor", 1));
        _dispatcher.Query(Arg.Any<GetPublisherEditions>(), Arg.Any<CancellationToken>())
            .Returns(new PublisherDetail([new EditionRow(5, 9, "Foo", null, BookFormat.Hardcover, null, null, 0)]));

        await vm.ToggleExpandAsync(1);
        Assert.Contains(1, vm.ExpandedPublisherIds);
        Assert.True(vm.DetailByPublisherId.ContainsKey(1));

        await vm.ToggleExpandAsync(1); // collapse
        Assert.DoesNotContain(1, vm.ExpandedPublisherIds);

        // Re-expand should NOT re-query — detail is cached.
        await vm.ToggleExpandAsync(1);
        await _dispatcher.Received(1).Query(Arg.Any<GetPublisherEditions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenameAsync_OnChanged_SetsMessage_InvalidatesDetail_AndReloads()
    {
        var vm = await LoadedVm(new PublisherRow(1, "Tor", 0));
        _dispatcher.Query(Arg.Any<GetPublisherEditions>(), Arg.Any<CancellationToken>())
            .Returns(PublisherDetail.Empty);
        await vm.ToggleExpandAsync(1);
        Assert.True(vm.DetailByPublisherId.ContainsKey(1));

        _dispatcher.Send(Arg.Any<RenamePublisher>(), Arg.Any<CancellationToken>())
            .Returns(PublisherAdminResult.Done("Renamed to \"Tor Books\"."));

        await vm.RenameAsync(1, "Tor Books");

        Assert.Equal("Renamed to \"Tor Books\".", vm.SuccessMessage);
        Assert.False(vm.DetailByPublisherId.ContainsKey(1)); // invalidated
        await _dispatcher.Received(2).Query(Arg.Any<GetPublisherList>(), Arg.Any<CancellationToken>()); // reloaded
    }

    [Fact]
    public async Task RenameAsync_OnRefused_SetsMessage_DoesNotReload()
    {
        var vm = await LoadedVm(new PublisherRow(1, "Tor Books", 0));
        _dispatcher.Send(Arg.Any<RenamePublisher>(), Arg.Any<CancellationToken>())
            .Returns(PublisherAdminResult.Refused("A publisher named \"Tor\" already exists."));

        await vm.RenameAsync(1, "Tor");

        Assert.Contains("already exists", vm.SuccessMessage);
        await _dispatcher.Received(1).Query(Arg.Any<GetPublisherList>(), Arg.Any<CancellationToken>()); // only the initial load
    }

    [Fact]
    public async Task MergeAsync_OnChanged_InvalidatesBothRows()
    {
        var vm = await LoadedVm(new PublisherRow(1, "Tor Books", 2), new PublisherRow(2, "Tor", 0));
        _dispatcher.Query(Arg.Any<GetPublisherEditions>(), Arg.Any<CancellationToken>())
            .Returns(PublisherDetail.Empty);
        await vm.ToggleExpandAsync(1);
        await vm.ToggleExpandAsync(2);

        _dispatcher.Send(Arg.Any<MergePublishers>(), Arg.Any<CancellationToken>())
            .Returns(PublisherAdminResult.Done("Merged \"Tor Books\" into \"Tor\" — 2 editions reassigned."));

        await vm.MergeAsync(1, 2);

        Assert.False(vm.DetailByPublisherId.ContainsKey(1));
        Assert.False(vm.DetailByPublisherId.ContainsKey(2));
        await _dispatcher.Received(1).Send(
            Arg.Is<MergePublishers>(c => c.SourceId == 1 && c.TargetId == 2),
            Arg.Any<CancellationToken>());
    }
}
