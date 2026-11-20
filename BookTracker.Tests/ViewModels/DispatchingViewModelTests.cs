using BookTracker.Application;
using BookTracker.Data;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

// The shared Dispatch wrapper: a domain rule or not-found outcome lands in
// Error; anything else is a fault and is rethrown.
[Trait("Category", TestCategories.Unit)]
public class DispatchingViewModelTests
{
    private sealed class Vm : DispatchingViewModel
    {
        public Task<bool> Run(Func<Task> dispatch) => DispatchAsync(dispatch);
        public Task<int> RunWithResult(Func<Task<int>> dispatch) => DispatchAsync(dispatch);
    }

    [Fact]
    public async Task DomainRuleFailure_setsError_andReturnsFalse()
    {
        var vm = new Vm();

        var ok = await vm.Run(() => throw new DomainRuleException("Name is required."));

        Assert.False(ok);
        Assert.Equal("Name is required.", vm.Error);
    }

    [Fact]
    public async Task NotFound_setsError_andReturnsFalse()
    {
        var vm = new Vm();

        var ok = await vm.Run(() => throw new NotFoundException("Book 7 not found."));

        Assert.False(ok);
        Assert.Equal("Book 7 not found.", vm.Error);
    }

    [Fact]
    public async Task ResultOverload_returnsValue_onSuccess_andDefault_onRefusal()
    {
        var vm = new Vm();

        Assert.Equal(42, await vm.RunWithResult(() => Task.FromResult(42)));
        Assert.Null(vm.Error);

        Assert.Equal(0, await vm.RunWithResult(() => throw new DomainRuleException("no")));
        Assert.Equal("no", vm.Error);
    }

    [Fact]
    public async Task Fault_isRethrown_andLeavesErrorNull()
    {
        var vm = new Vm();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => vm.Run(() => throw new InvalidOperationException("boom")));

        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Error_isClearedOnTheNextAttempt()
    {
        var vm = new Vm();
        await vm.Run(() => throw new DomainRuleException("first"));
        Assert.Equal("first", vm.Error);

        var ok = await vm.Run(() => Task.CompletedTask);

        Assert.True(ok);
        Assert.Null(vm.Error);
    }
}
