using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using BookTracker.Data;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookTracker.Tests.ViewModels;

// The dialog reads through GetCopyForEdit and writes through AddCopyToEdition /
// UpdateCopy; a substituted dispatcher stands in for all three. The read's SQL
// is covered by GetCopyForEditHandlerTests, the writes' persistence by
// BookCommandHandlersTests.
[Trait("Category", TestCategories.Unit)]
public class CopyFormDialogViewModelTests
{
    private const int CopyId = 11;
    private const int EditionId = 5;
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private void GivenCopy(CopyForEdit? copy) =>
        _dispatcher.Query(Arg.Is<GetCopyForEdit>(q => q!.CopyId == CopyId), Arg.Any<CancellationToken>())
            .Returns(copy);

    [Fact]
    public void InitializeForAdd_SetsIsNewAndDefaults()
    {
        var vm = new CopyFormDialogViewModel(_dispatcher);

        vm.InitializeForAdd(42);

        Assert.True(vm.IsNew);
        Assert.Equal(42, vm.EditionId);
        Assert.Null(vm.CopyId);
        Assert.NotNull(vm.DateAcquired); // today, by default
    }

    [Fact]
    public async Task InitializeForEditAsync_MissingId_MarksNotFound()
    {
        GivenCopy(null);
        var vm = new CopyFormDialogViewModel(_dispatcher);

        await vm.InitializeForEditAsync(CopyId);

        Assert.True(vm.NotFound);
    }

    [Fact]
    public async Task InitializeForEditAsync_RemovedCopy_SetsTheRemovalLabel()
    {
        GivenCopy(new CopyForEdit(CopyId, EditionId, BookCondition.Good, null, null,
            CopyDisposition.Donated, new DateOnly(2026, 11, 3)));
        var vm = new CopyFormDialogViewModel(_dispatcher);

        await vm.InitializeForEditAsync(CopyId);

        Assert.Equal("Donated · 3 Nov 2026", vm.RemovalLabel);
    }

    [Fact]
    public async Task InitializeForEditAsync_OwnedCopy_HasNoRemovalLabel()
    {
        GivenCopy(new CopyForEdit(CopyId, EditionId, BookCondition.Good, null, null));
        var vm = new CopyFormDialogViewModel(_dispatcher);

        await vm.InitializeForEditAsync(CopyId);

        Assert.Null(vm.RemovalLabel);
    }

    private async Task<CopyFormDialogViewModel> EditVmAsync(
        CopyDisposition disposition = CopyDisposition.Owned, bool isLent = false)
    {
        GivenCopy(new CopyForEdit(CopyId, EditionId, BookCondition.Good, null, null,
            disposition, disposition == CopyDisposition.Owned ? null : new DateOnly(2026, 11, 3), isLent));
        var vm = new CopyFormDialogViewModel(_dispatcher);
        await vm.InitializeForEditAsync(CopyId);
        return vm;
    }

    [Fact]
    public async Task CanRemove_onlyForAnOwnedCopyNotOnLoan()
    {
        Assert.True((await EditVmAsync()).CanRemove);
        Assert.False((await EditVmAsync(isLent: true)).CanRemove);
        Assert.False((await EditVmAsync(CopyDisposition.Lost)).CanRemove);
    }

    [Fact]
    public void CanRemoveAndCanRestore_notOfferedInAddMode()
    {
        var vm = new CopyFormDialogViewModel(_dispatcher);
        vm.InitializeForAdd(EditionId);

        Assert.False(vm.CanRemove);
        Assert.False(vm.CanRestore);
    }

    [Fact]
    public async Task CanRestore_onlyForARemovedCopy()
    {
        Assert.True((await EditVmAsync(CopyDisposition.Sold)).CanRestore);
        Assert.False((await EditVmAsync()).CanRestore);
    }

    [Fact]
    public async Task IsDirty_tracksEditsAgainstTheLoadedCopy()
    {
        var vm = await EditVmAsync();
        Assert.False(vm.IsDirty);

        vm.Notes = "signed";
        Assert.True(vm.IsDirty);

        vm.Notes = "";
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task RestoreAsync_sendsRestoreCopyToCollection()
    {
        var vm = await EditVmAsync(CopyDisposition.Donated);

        Assert.True(await vm.RestoreAsync());

        await _dispatcher.Received(1).Send(Arg.Is<RestoreCopyToCollection>(c => c!.CopyId == CopyId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_refused_setsError_andReturnsFalse()
    {
        var vm = await EditVmAsync(CopyDisposition.Donated);
        _dispatcher.Send(Arg.Any<RestoreCopyToCollection>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("This copy is already in the collection."));

        Assert.False(await vm.RestoreAsync());

        Assert.Equal("This copy is already in the collection.", vm.Error);
    }

    [Fact]
    public async Task RestoreAsync_ownedCopy_sendsNothing()
    {
        var vm = await EditVmAsync();

        Assert.False(await vm.RestoreAsync());
        await _dispatcher.DidNotReceive().Send(Arg.Any<RestoreCopyToCollection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_withUnsavedEdits_sendsNothing()
    {
        var vm = await EditVmAsync(CopyDisposition.Donated);
        vm.Notes = "signed";

        Assert.False(await vm.RestoreAsync());
        await _dispatcher.DidNotReceive().Send(Arg.Any<RestoreCopyToCollection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeForEditAsync_LoadsCopyFields()
    {
        GivenCopy(new CopyForEdit(CopyId, EditionId, BookCondition.VeryGood, new DateTime(2024, 3, 15), "From a charity shop"));
        var vm = new CopyFormDialogViewModel(_dispatcher);

        await vm.InitializeForEditAsync(CopyId);

        Assert.False(vm.IsNew);
        Assert.False(vm.NotFound);
        Assert.Equal(CopyId, vm.CopyId);
        Assert.Equal(EditionId, vm.EditionId);
        Assert.Equal(BookCondition.VeryGood, vm.Condition);
        Assert.Equal(new DateTime(2024, 3, 15), vm.DateAcquired);
        Assert.Equal("From a charity shop", vm.Notes);
    }

    [Fact]
    public async Task SaveAsync_Add_SendsAddCopyToEdition_AndReturnsNewId()
    {
        _dispatcher.Send(Arg.Any<AddCopyToEdition>(), Arg.Any<CancellationToken>()).Returns(99);
        var vm = new CopyFormDialogViewModel(_dispatcher);
        vm.InitializeForAdd(EditionId);
        vm.Condition = BookCondition.Fine;
        vm.Notes = "  signed  ";

        var id = await vm.SaveAsync();

        Assert.Equal(99, id);
        await _dispatcher.Received(1).Send(
            Arg.Is<AddCopyToEdition>(c => c!.EditionId == EditionId
                && c.Condition == BookCondition.Fine
                && c.Notes == "  signed  "), // the handler trims
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_Edit_SendsUpdateCopy_AndReturnsCopyId()
    {
        GivenCopy(new CopyForEdit(CopyId, EditionId, BookCondition.Good, null, "old"));
        var vm = new CopyFormDialogViewModel(_dispatcher);
        await vm.InitializeForEditAsync(CopyId);
        vm.Condition = BookCondition.Fair;
        vm.Notes = "";

        var id = await vm.SaveAsync();

        Assert.Equal(CopyId, id);
        await _dispatcher.Received(1).Send(
            Arg.Is<UpdateCopy>(c => c!.CopyId == CopyId
                && c.Condition == BookCondition.Fair
                && c.Notes == ""), // the handler stores blank as null
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_NotFound_IsNoOp()
    {
        GivenCopy(null);
        var vm = new CopyFormDialogViewModel(_dispatcher);
        await vm.InitializeForEditAsync(CopyId);

        Assert.Null(await vm.SaveAsync());
        await _dispatcher.DidNotReceive().Send(Arg.Any<UpdateCopy>(), Arg.Any<CancellationToken>());
    }
}
