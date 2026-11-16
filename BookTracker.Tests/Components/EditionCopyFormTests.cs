using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Publishers;
using BookTracker.Web.Components.Shared;
using BookTracker.Web.ViewModels;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookTracker.Tests.Components;

/// <summary>
/// bUnit tests for EditionCopyForm's publisher field wiring (TD-15a). The commit
/// gesture is now an explicit selection on the CreatableAutocomplete (an existing
/// pick or the "Add …" row) — not commit-on-blur. OnPublisherChosenAsync stores
/// the committed name on EditionInput.Publisher (the save resolves it via the
/// PublisherResolver net) and delegates the skip-existing / dispatch-new /
/// swallow-fault branch to the shared PublisherEagerCreate helper (covered
/// exhaustively in <see cref="PublisherEagerCreateTests"/>); a new name is also
/// appended to the cache. These cover the component-side wiring.
///
/// EditionCopyForm.OnInitializedAsync loads the existing publishers through
/// its EditionFormViewModel, which queries GetPublisherOptions; the substituted
/// dispatcher answers that (empty unless a test seeds it) and the eager create.
/// </summary>
[Trait("Category", TestCategories.Component)]
public class EditionCopyFormTests : ComponentTestBase
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    public EditionCopyFormTests()
    {
        GivenPublishers();
        Services.AddSingleton(_dispatcher);
        Services.AddLogging();
        Services.AddScoped<EditionFormViewModel>();
    }

    private void GivenPublishers(params PublisherOption[] publishers) =>
        _dispatcher.Query(Arg.Any<GetPublisherOptions>(), Arg.Any<CancellationToken>())
            .Returns(publishers);

    private (EditionFormViewModel.EditionFormInput Edition, CopyFormViewModel.CopyFormInput Copy) Inputs()
        => (new EditionFormViewModel.EditionFormInput(), new CopyFormViewModel.CopyFormInput());

    private IRenderedComponent<EditionCopyForm> RenderForm(EditionFormViewModel.EditionFormInput edition, CopyFormViewModel.CopyFormInput copy)
        => Render<EditionCopyForm>(p => p
            .Add(c => c.EditionInput, edition)
            .Add(c => c.CopyInput, copy));

    [Fact]
    public async Task PublisherChosen_NewName_StoresValueEagerCreatesAndCaches()
    {
        _dispatcher.Send(Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>()).Returns((int?)42);
        var (edition, copy) = Inputs();
        var cut = RenderForm(edition, copy);

        await cut.InvokeAsync(() => cut.Instance.OnPublisherChosenAsync("Gollancz"));

        Assert.Equal("Gollancz", edition.Publisher); // stored for the save
        await _dispatcher.Received(1).Send(
            Arg.Is<CreatePublisher>(c => c!.Name == "Gollancz"), Arg.Any<CancellationToken>());
        // Appended to the cache so a re-pick is a no-op and it shows in search.
        var vm = Services.GetRequiredService<EditionFormViewModel>();
        Assert.Contains(vm.ExistingPublishers, p => p.Name == "Gollancz");
    }

    [Fact]
    public async Task PublisherChosen_ExistingName_StoresValueWithNoDispatch()
    {
        // An existing publisher; OnInitializedAsync caches it. Picking it
        // (even with a case clash) is a pure pick — no CreatePublisher round-trip.
        GivenPublishers(new PublisherOption(1, "Penguin Books"));
        var (edition, copy) = Inputs();
        var cut = RenderForm(edition, copy);

        await cut.InvokeAsync(() => cut.Instance.OnPublisherChosenAsync("penguin books"));

        Assert.Equal("penguin books", edition.Publisher);
        await _dispatcher.DidNotReceive().Send(
            Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublisherChosen_Cleared_ClearsValueWithNoDispatch()
    {
        var (edition, copy) = Inputs();
        edition.Publisher = "Gollancz";
        var cut = RenderForm(edition, copy);

        await cut.InvokeAsync(() => cut.Instance.OnPublisherChosenAsync(null));

        Assert.Null(edition.Publisher);
        await _dispatcher.DidNotReceive().Send(
            Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublisherChosen_EagerCreateThrows_DoesNotSurface()
    {
        // Best-effort: a faulting dispatch must not throw out of the handler — the
        // save's PublisherResolver net guarantees the row. The value is still stored.
        _dispatcher.Send(Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int?>(new InvalidOperationException("transient")));
        var (edition, copy) = Inputs();
        var cut = RenderForm(edition, copy);

        await cut.InvokeAsync(() => cut.Instance.OnPublisherChosenAsync("Gollancz"));

        Assert.Equal("Gollancz", edition.Publisher);
    }
}
