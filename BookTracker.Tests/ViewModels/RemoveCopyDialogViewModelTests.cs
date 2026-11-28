using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookTracker.Tests.ViewModels;

// The remove dialog writes through RemoveCopyFromCollection; a substituted
// dispatcher stands in. The rules themselves are covered by the Copy entity
// tests and CopyDispositionHandlersTests.
[Trait("Category", TestCategories.Unit)]
public class RemoveCopyDialogViewModelTests
{
    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // 14:00 UTC on 19 Nov is 20 Nov in Melbourne.
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 11, 19, 14, 0, 0, TimeSpan.Zero));

    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private RemoveCopyDialogViewModel InitializedVm()
    {
        var vm = new RemoveCopyDialogViewModel(_dispatcher, Clock);
        vm.Initialize(11, "Dune · Hardcover");
        return vm;
    }

    [Fact]
    public void Initialize_defaultsTheDateToToday_withNoReason()
    {
        var vm = InitializedVm();

        Assert.Equal(new DateTime(2026, 11, 20), vm.RemovedOn);
        Assert.Null(vm.Reason);
        Assert.Null(vm.Note);
        Assert.Null(vm.Error);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void Reasons_leaveOutOwned()
    {
        Assert.Equal(
            [CopyDisposition.Donated, CopyDisposition.Lost, CopyDisposition.Damaged, CopyDisposition.Sold],
            RemoveCopyDialogViewModel.Reasons);
    }

    [Fact]
    public void CanConfirm_needsAReasonAndADate()
    {
        var vm = InitializedVm();

        vm.Reason = CopyDisposition.Sold;
        Assert.True(vm.CanConfirm);

        vm.RemovedOn = null;
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void CanConfirm_refusesOwnedAsAReason()
    {
        var vm = InitializedVm();
        vm.Reason = CopyDisposition.Owned;

        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void CanConfirm_noteAtTheLimitPasses_oneOverFails_andSpacesDoNotCount()
    {
        var vm = InitializedVm();
        vm.Reason = CopyDisposition.Lost;

        vm.Note = new string('x', Copy.RemovalNoteMaxLength);
        Assert.True(vm.CanConfirm);

        vm.Note = new string('x', Copy.RemovalNoteMaxLength + 1);
        Assert.False(vm.CanConfirm);

        vm.Note = new string('x', Copy.RemovalNoteMaxLength) + "   ";
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public async Task ConfirmAsync_sendsRemoveCopyFromCollection()
    {
        var vm = InitializedVm();
        vm.Reason = CopyDisposition.Damaged;
        vm.RemovedOn = new DateTime(2026, 11, 3, 15, 30, 0);
        vm.Note = "Water damage";

        var ok = await vm.ConfirmAsync();

        Assert.True(ok);
        Assert.Null(vm.Error);
        await _dispatcher.Received(1).Send(
            Arg.Is<RemoveCopyFromCollection>(c =>
                c!.CopyId == 11 && c.Reason == CopyDisposition.Damaged
                && c.RemovedOn == new DateOnly(2026, 11, 3) && c.Note == "Water damage"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmAsync_cannotConfirm_sendsNothing()
    {
        var vm = InitializedVm();

        Assert.False(await vm.ConfirmAsync());
        await _dispatcher.DidNotReceive().Send(Arg.Any<RemoveCopyFromCollection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmAsync_refusedRule_setsError_andReturnsFalse()
    {
        _dispatcher.Send(Arg.Any<RemoveCopyFromCollection>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("Return this copy before removing it."));
        var vm = InitializedVm();
        vm.Reason = CopyDisposition.Donated;

        var ok = await vm.ConfirmAsync();

        Assert.False(ok);
        Assert.Equal("Return this copy before removing it.", vm.Error);
        Assert.False(vm.Saving);
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public void RemovalLabel_namesTheReasonAndDate()
    {
        Assert.Equal("Donated · 3 Nov 2026", RemoveCopyDialogViewModel.RemovalLabel(CopyDisposition.Donated, new DateOnly(2026, 11, 3)));
        Assert.Equal("Lost", RemoveCopyDialogViewModel.RemovalLabel(CopyDisposition.Lost, null));
    }
}
