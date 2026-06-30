using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Data.Models;
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
/// Integration-flavoured: EditionCopyForm.OnInitializedAsync loads existing
/// publishers from the DB, so it needs a real <see cref="TestDbContextFactory"/>
/// (wiped empty, then optionally seeded per test) behind its EditionFormViewModel.
/// </summary>
[Trait("Category", TestCategories.Integration)]
public class EditionCopyFormTests : ComponentTestBase
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();
    private readonly TestDbContextFactory _factory = new();

    public EditionCopyFormTests()
    {
        Services.AddSingleton(_dispatcher);
        Services.AddLogging();
        // Reuse the one (already-wiped) factory so per-test seeding survives —
        // a fresh TestDbContextFactory would wipe again on construction. The
        // eager-create path under test goes through the substituted dispatcher.
        Services.AddScoped(_ => new EditionFormViewModel(_factory));
    }

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
            Arg.Is<CreatePublisher>(c => c.Name == "Gollancz"), Arg.Any<CancellationToken>());
        // Appended to the cache so a re-pick is a no-op and it shows in search.
        var vm = Services.GetRequiredService<EditionFormViewModel>();
        Assert.Contains(vm.ExistingPublishers, p => p.Name == "Gollancz");
    }

    [Fact]
    public async Task PublisherChosen_ExistingName_StoresValueWithNoDispatch()
    {
        // Seed an existing publisher; OnInitializedAsync caches it. Picking it
        // (even with a case clash) is a pure pick — no CreatePublisher round-trip.
        await using (var db = _factory.CreateDbContext())
        {
            db.Publishers.Add(new Publisher { Name = "Penguin Books" });
            await db.SaveChangesAsync();
        }
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
