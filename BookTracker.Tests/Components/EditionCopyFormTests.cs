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
/// bUnit tests for EditionCopyForm's publisher eager-create (TD-15a). The
/// commit gesture is the field losing focus (OnBlur) — NOT per-keystroke
/// ValueChanged, which fired on every character and created a publisher row
/// for each partial string. On blur, a name not already in the cached list is
/// eager-created via <see cref="CreatePublisher"/> (best-effort); an existing
/// name or blank is a no-op (no wire call). @bind-Value handles the field value
/// itself, so these tests drive the eager-create method directly.
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
    public async Task PublisherBlur_NewName_EagerCreates()
    {
        var (edition, copy) = Inputs();
        var cut = RenderForm(edition, copy);

        await cut.InvokeAsync(() => cut.Instance.EagerCreatePublisherIfNewAsync("Gollancz"));

        await _dispatcher.Received(1).Send(
            Arg.Is<CreatePublisher>(c => c.Name == "Gollancz"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublisherBlur_ExistingName_DoesNotEagerCreate()
    {
        // Seed an existing publisher; OnInitializedAsync caches it. Committing it
        // (even with a case clash) is a pure pick — no CreatePublisher round-trip.
        await using (var db = _factory.CreateDbContext())
        {
            db.Publishers.Add(new Publisher { Name = "Penguin Books" });
            await db.SaveChangesAsync();
        }
        var (edition, copy) = Inputs();
        var cut = RenderForm(edition, copy);

        await cut.InvokeAsync(() => cut.Instance.EagerCreatePublisherIfNewAsync("penguin books"));

        await _dispatcher.DidNotReceive().Send(
            Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublisherBlur_Blank_DoesNotEagerCreate(string? value)
    {
        var (edition, copy) = Inputs();
        var cut = RenderForm(edition, copy);

        await cut.InvokeAsync(() => cut.Instance.EagerCreatePublisherIfNewAsync(value));

        await _dispatcher.DidNotReceive().Send(
            Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublisherBlur_EagerCreateThrows_DoesNotSurface()
    {
        // Best-effort: a faulting dispatch must not throw out of the blur handler
        // — the save's PublisherResolver net guarantees the row.
        _dispatcher.Send(Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int?>(new InvalidOperationException("transient")));
        var (edition, copy) = Inputs();
        var cut = RenderForm(edition, copy);

        // Should complete without throwing.
        await cut.InvokeAsync(() => cut.Instance.EagerCreatePublisherIfNewAsync("Gollancz"));
    }
}
