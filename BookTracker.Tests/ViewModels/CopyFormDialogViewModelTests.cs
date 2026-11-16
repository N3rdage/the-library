using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using NSubstitute;

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
