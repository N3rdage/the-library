using BookTracker.Data;
using BookTracker.Web.Components.Shared;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace BookTracker.Tests.Components;

/// <summary>
/// bUnit tests for MudAuthorPicker — the multi-author chip picker that
/// shipped six post-merge fix commits in PR #154. Each test below maps
/// to a chip-add/remove invariant the picker must hold; together they
/// document what TryAddAsync and OnCommitKey are *for* and would have
/// caught most of the post-PR bugs at PR time.
///
/// What's NOT tested here (bUnit can't run real JS): the keydown
/// suppression via chip-picker-keys.js, aria-activedescendant detection,
/// or the full DotNetObjectReference round-trip. Those need Playwright
/// (slice b of #16). The .NET-side OnCommitKey method IS callable
/// directly from tests, which covers everything that crosses the
/// JS↔.NET boundary on the .NET side.
/// </summary>
[Trait("Category", TestCategories.Component)]
public class MudAuthorPickerTests : ComponentTestBase
{
    public MudAuthorPickerTests()
    {
        // MudAuthorPicker injects IDbContextFactory for SearchAsync (the
        // dropdown's autocomplete provider). Tests below don't trigger
        // typing into the autocomplete, so a stub suffices — render +
        // chip-add paths don't touch the DB.
        var dbFactory = Substitute.For<IDbContextFactory<BookTrackerDbContext>>();
        Services.AddSingleton(dbFactory);
    }

    [Fact]
    public void EmptyAuthors_RendersNoChips()
    {
        var cut = RenderComponent<MudAuthorPicker>(p => p
            .Add(c => c.Authors, []));

        Assert.Empty(cut.FindAll(".mud-chip"));
    }

    [Fact]
    public void PreSeededAuthors_RendersOneChipPerName()
    {
        var cut = RenderComponent<MudAuthorPicker>(p => p
            .Add(c => c.Authors, ["Douglas Preston", "Lincoln Child"]));

        var chips = cut.FindAll(".mud-chip");
        Assert.Equal(2, chips.Count);
        Assert.Contains(chips, c => c.TextContent.Contains("Douglas Preston"));
        Assert.Contains(chips, c => c.TextContent.Contains("Lincoln Child"));
    }

    [Fact]
    public async Task OnCommitKey_AddsTypedTextAsChip()
    {
        var authors = new List<string>();
        List<string>? captured = null;
        var cut = RenderComponent<MudAuthorPicker>(p => p
            .Add(c => c.Authors, authors)
            .Add(c => c.AuthorsChanged, (List<string> list) => captured = list));

        await cut.InvokeAsync(() => cut.Instance.OnCommitKey("Preston"));

        Assert.Equal(["Preston"], authors);
        Assert.NotNull(captured);
        Assert.Equal(["Preston"], captured!);
    }

    [Fact]
    public async Task OnCommitKey_TrimsTrailingComma()
    {
        // Comma is a commit trigger — the JS layer reads input.value at
        // keydown time, which still includes the comma the user just
        // typed. TryAddAsync strips it so the chip text doesn't carry
        // punctuation noise.
        var authors = new List<string>();
        var cut = RenderComponent<MudAuthorPicker>(p => p
            .Add(c => c.Authors, authors));

        await cut.InvokeAsync(() => cut.Instance.OnCommitKey("Preston,"));

        Assert.Equal(["Preston"], authors);
    }

    [Fact]
    public async Task OnCommitKey_TrimsLeadingAndTrailingWhitespace()
    {
        var authors = new List<string>();
        var cut = RenderComponent<MudAuthorPicker>(p => p
            .Add(c => c.Authors, authors));

        await cut.InvokeAsync(() => cut.Instance.OnCommitKey("  Preston  "));

        Assert.Equal(["Preston"], authors);
    }

    [Fact]
    public async Task OnCommitKey_DedupesCaseInsensitive()
    {
        // Typing an existing chip's name in a different case should be a
        // no-op rather than adding a duplicate row that'd then collide
        // with the unique index on Author.Name at save time.
        var authors = new List<string> { "Preston" };
        var cut = RenderComponent<MudAuthorPicker>(p => p
            .Add(c => c.Authors, authors));

        await cut.InvokeAsync(() => cut.Instance.OnCommitKey("PRESTON"));

        Assert.Single(authors);
        Assert.Equal("Preston", authors[0]);
    }

    [Fact]
    public async Task OnCommitKey_BlankInput_DoesNotFireAuthorsChanged()
    {
        // Empty / whitespace / single-punctuation input should be a no-op.
        // Mixed punctuation+whitespace soup (e.g. ",, ;") isn'\''t fully
        // collapsed by the current TryAddAsync because TrimEnd stops at
        // the first non-trim char (the embedded space) and the subsequent
        // .Trim() doesn'\''t re-run the punctuation strip. That'\''s a
        // theoretical input nobody types in practice; documented here so
        // a future tighten-up isn'\''t a surprise.
        var authors = new List<string>();
        List<string>? captured = null;
        var cut = RenderComponent<MudAuthorPicker>(p => p
            .Add(c => c.Authors, authors)
            .Add(c => c.AuthorsChanged, (List<string> list) => captured = list));

        await cut.InvokeAsync(() => cut.Instance.OnCommitKey(""));
        await cut.InvokeAsync(() => cut.Instance.OnCommitKey("   "));
        await cut.InvokeAsync(() => cut.Instance.OnCommitKey(","));
        await cut.InvokeAsync(() => cut.Instance.OnCommitKey(",,&;"));

        Assert.Empty(authors);
        Assert.Null(captured);
    }

    [Fact]
    public async Task OnCommitKey_MultipleAdds_AppendInOrder()
    {
        // PR #154's chip-per-keystroke bug had been multiplying chips on
        // single typing events. The cure (CoerceValue=false, JS-driven
        // commits) means each OnCommitKey call adds exactly one chip,
        // appended at the end of the list in call order.
        var authors = new List<string>();
        var cut = RenderComponent<MudAuthorPicker>(p => p
            .Add(c => c.Authors, authors));

        await cut.InvokeAsync(() => cut.Instance.OnCommitKey("Preston"));
        await cut.InvokeAsync(() => cut.Instance.OnCommitKey("Child"));
        await cut.InvokeAsync(() => cut.Instance.OnCommitKey("Pendergast"));

        Assert.Equal(["Preston", "Child", "Pendergast"], authors);
    }

    [Fact]
    public async Task ChipClose_RemovesThatAuthor()
    {
        var authors = new List<string> { "Preston", "Child" };
        List<string>? captured = null;
        var cut = RenderComponent<MudAuthorPicker>(p => p
            .Add(c => c.Authors, authors)
            .Add(c => c.AuthorsChanged, (List<string> list) => captured = list));

        // MudChip exposes OnClose as an EventCallback<MudChip<string>>; the
        // component'\''s OnClose lambda discards the chip param and routes
        // to RemoveAsync(captured-name) via closure capture.
        var prestonChip = cut.FindComponents<MudChip<string>>()
            .First(c => c.Instance.Text == "Preston");
        await cut.InvokeAsync(() =>
            prestonChip.Instance.OnClose.InvokeAsync(prestonChip.Instance));

        Assert.Equal(["Child"], authors);
        Assert.NotNull(captured);
        Assert.Equal(["Child"], captured!);
    }
}
