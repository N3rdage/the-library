using BookTracker.Application;
using BookTracker.Application.Authors;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

// Thin unit tests for the VM-side of /authors/{id}: the not-found mapping, the
// dispatch guards, and the success/error toast + reload wiring. The data reads
// and write effects are covered by GetAuthorDetailHandlerTests /
// AuthorAdminCommandsTests.
[Trait("Category", TestCategories.Unit)]
public class AuthorDetailViewModelTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private static AuthorDetailResult Result(int id, string name, int? canonicalId = null, string? canonicalName = null) =>
        new(new AuthorHeader(id, name, canonicalId, canonicalName), AuthorDetail.Empty, []);

    private void StubDetail(int id, AuthorDetailResult? result) =>
        _dispatcher.Query(Arg.Is<GetAuthorDetail>(q => q.AuthorId == id), Arg.Any<CancellationToken>())
            .Returns(result);

    [Fact]
    public async Task LoadAsync_NullResult_SetsNotFound()
    {
        StubDetail(99999, null);
        var vm = new AuthorDetailViewModel(_dispatcher);

        await vm.LoadAsync(99999);

        Assert.True(vm.NotFound);
        Assert.Null(vm.Header);
    }

    [Fact]
    public async Task LoadAsync_PopulatesHeaderAndDetail()
    {
        StubDetail(1, Result(1, "Stephen King"));
        var vm = new AuthorDetailViewModel(_dispatcher);

        await vm.LoadAsync(1);

        Assert.False(vm.NotFound);
        Assert.Equal("Stephen King", vm.Header!.Name);
    }

    [Fact]
    public async Task RenameAsync_OnSuccess_SetsMessageAndReloads()
    {
        StubDetail(1, Result(1, "Old"));
        _dispatcher.Send(Arg.Any<RenameAuthor>(), Arg.Any<CancellationToken>())
            .Returns(AuthorAdminResult.Ok("Renamed to \"New\"."));
        var vm = new AuthorDetailViewModel(_dispatcher);
        await vm.LoadAsync(1);

        await vm.RenameAsync("New");

        Assert.Equal("Renamed to \"New\".", vm.SuccessMessage);
        // Reload = a second GetAuthorDetail dispatch for the same id.
        await _dispatcher.Received(2).Query(Arg.Is<GetAuthorDetail>(q => q.AuthorId == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenameAsync_OnError_SetsErrorAndDoesNotReload()
    {
        StubDetail(1, Result(1, "Alice"));
        _dispatcher.Send(Arg.Any<RenameAuthor>(), Arg.Any<CancellationToken>())
            .Returns(AuthorAdminResult.Error("An author named \"Bob\" already exists."));
        var vm = new AuthorDetailViewModel(_dispatcher);
        await vm.LoadAsync(1);

        await vm.RenameAsync("Bob");

        Assert.NotNull(vm.ErrorMessage);
        // No reload — still only the initial load dispatch.
        await _dispatcher.Received(1).Query(Arg.Is<GetAuthorDetail>(q => q.AuthorId == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenameAsync_NoHeader_DoesNotDispatch()
    {
        var vm = new AuthorDetailViewModel(_dispatcher);

        await vm.RenameAsync("Whatever"); // never loaded → Header null

        await _dispatcher.DidNotReceive().Send(Arg.Any<RenameAuthor>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAsAliasOfAsync_TargetIsSelf_DoesNotDispatch()
    {
        StubDetail(1, Result(1, "Self"));
        var vm = new AuthorDetailViewModel(_dispatcher);
        await vm.LoadAsync(1);

        await vm.MarkAsAliasOfAsync(1); // canonicalId == Header.Id

        await _dispatcher.DidNotReceive().Send(Arg.Any<MarkAuthorAsAliasOf>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAsAliasOfAsync_Valid_DispatchesAndReloads()
    {
        StubDetail(2, Result(2, "Bachman"));
        _dispatcher.Send(Arg.Any<MarkAuthorAsAliasOf>(), Arg.Any<CancellationToken>())
            .Returns(AuthorAdminResult.Ok("linked"));
        var vm = new AuthorDetailViewModel(_dispatcher);
        await vm.LoadAsync(2);

        await vm.MarkAsAliasOfAsync(1);

        await _dispatcher.Received(1).Send(
            Arg.Is<MarkAuthorAsAliasOf>(c => c.AliasId == 2 && c.CanonicalId == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteToCanonicalAsync_AlreadyCanonical_DoesNotDispatch()
    {
        StubDetail(1, Result(1, "Canonical", canonicalId: null));
        var vm = new AuthorDetailViewModel(_dispatcher);
        await vm.LoadAsync(1);

        await vm.PromoteToCanonicalAsync(); // Header.CanonicalAuthorId is null

        await _dispatcher.DidNotReceive().Send(Arg.Any<PromoteAuthorToCanonical>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteToCanonicalAsync_Alias_DispatchesPromote()
    {
        StubDetail(2, Result(2, "Bachman", canonicalId: 1, canonicalName: "King"));
        _dispatcher.Send(Arg.Any<PromoteAuthorToCanonical>(), Arg.Any<CancellationToken>())
            .Returns(AuthorAdminResult.Ok("promoted"));
        var vm = new AuthorDetailViewModel(_dispatcher);
        await vm.LoadAsync(2);

        await vm.PromoteToCanonicalAsync();

        await _dispatcher.Received(1).Send(
            Arg.Is<PromoteAuthorToCanonical>(c => c.AuthorId == 2),
            Arg.Any<CancellationToken>());
    }
}
