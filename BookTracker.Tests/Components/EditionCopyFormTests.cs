using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Web.Components.Shared;
using BookTracker.Web.ViewModels;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookTracker.Tests.Components;

/// <summary>
/// bUnit tests for EditionCopyForm's publisher eager-create wiring (TD-15a).
/// The publisher MudAutocomplete commits via ValueChanged (pick or coerced
/// free text), which stores the value and dispatches <see cref="CreatePublisher"/>
/// best-effort so the row exists immediately. Mirrors the author-picker
/// pattern in <see cref="MudAuthorPickerTests"/>.
///
/// Integration-flavoured: EditionCopyForm.OnInitializedAsync loads existing
/// publishers from the DB, so it needs a real <see cref="TestDbContextFactory"/>
/// (wiped empty) behind its EditionFormViewModel rather than the DB-free stub
/// the chip-only author picker gets.
/// </summary>
[Trait("Category", TestCategories.Integration)]
public class EditionCopyFormTests : ComponentTestBase
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    public EditionCopyFormTests()
    {
        Services.AddSingleton(_dispatcher);
        Services.AddLogging();
        // Real (wiped-empty) DB behind the VM so InitializeAsync's publisher
        // load succeeds; the eager-create path under test goes through the
        // substituted dispatcher, not this factory.
        Services.AddScoped(_ => new EditionFormViewModel(new TestDbContextFactory()));
    }

    private (EditionFormViewModel.EditionFormInput Edition, CopyFormViewModel.CopyFormInput Copy) Inputs()
        => (new EditionFormViewModel.EditionFormInput(), new CopyFormViewModel.CopyFormInput());

    [Fact]
    public async Task PublisherCommit_NewName_StoresValueAndEagerCreates()
    {
        var (edition, copy) = Inputs();
        var cut = Render<EditionCopyForm>(p => p
            .Add(c => c.EditionInput, edition)
            .Add(c => c.CopyInput, copy));

        await cut.InvokeAsync(() => cut.Instance.OnPublisherCommittedAsync("Gollancz"));

        Assert.Equal("Gollancz", edition.Publisher);
        await _dispatcher.Received(1).Send(
            Arg.Is<CreatePublisher>(c => c.Name == "Gollancz"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublisherCommit_Blank_StoresValueButDoesNotEagerCreate()
    {
        var (edition, copy) = Inputs();
        var cut = Render<EditionCopyForm>(p => p
            .Add(c => c.EditionInput, edition)
            .Add(c => c.CopyInput, copy));

        await cut.InvokeAsync(() => cut.Instance.OnPublisherCommittedAsync("   "));

        Assert.Equal("   ", edition.Publisher);
        await _dispatcher.DidNotReceive().Send(
            Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublisherCommit_EagerCreateThrows_StillStoresValue()
    {
        // Best-effort: a faulting dispatch must not abort the field edit — the
        // save's PublisherResolver net guarantees the row.
        _dispatcher.Send(Arg.Any<CreatePublisher>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int?>(new InvalidOperationException("transient")));
        var (edition, copy) = Inputs();
        var cut = Render<EditionCopyForm>(p => p
            .Add(c => c.EditionInput, edition)
            .Add(c => c.CopyInput, copy));

        await cut.InvokeAsync(() => cut.Instance.OnPublisherCommittedAsync("Gollancz"));

        Assert.Equal("Gollancz", edition.Publisher);
    }
}
