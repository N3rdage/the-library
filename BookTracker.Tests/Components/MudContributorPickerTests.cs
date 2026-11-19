using BookTracker.Application;
using BookTracker.Application.Authors;
using BookTracker.Data.Models;
using BookTracker.Web.Components.Shared;
using BookTracker.Web.ViewModels;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace BookTracker.Tests.Components;

/// <summary>
/// bUnit tests for MudContributorPicker — the sibling of MudAuthorPicker for
/// non-Author contributors (editor, translator, …). Each chip carries a name and
/// a role; the pending row is a name autocomplete, a role select and an Add
/// button. The tests drive the pending row through the child components'
/// callbacks (the autocomplete's ValueChanged, the select's ValueChanged) and
/// click Add, then assert on the chip list and the eager CreateAuthor dispatch.
/// </summary>
[Trait("Category", TestCategories.Component)]
public class MudContributorPickerTests : ComponentTestBase
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    public MudContributorPickerTests()
    {
        // The picker queries SearchAuthorNames for the dropdown and dispatches
        // CreateAuthor on Add; a substitute stands in for both.
        Services.AddSingleton(_dispatcher);
        Services.AddLogging();
    }

    private IRenderedComponent<MudContributorPicker> RenderPicker(
        List<ContributorEntry> contributors, Action<List<ContributorEntry>>? onChanged = null)
        => Render<MudContributorPicker>(p =>
        {
            p.Add(c => c.Contributors, contributors);
            if (onChanged is not null)
                p.Add(c => c.ContributorsChanged, onChanged);
        });

    private static async Task TypeNameAsync(IRenderedComponent<MudContributorPicker> cut, string name)
    {
        var autocomplete = cut.FindComponent<MudAutocomplete<string>>();
        await cut.InvokeAsync(() => autocomplete.Instance.ValueChanged.InvokeAsync(name));
    }

    private static async Task PickRoleAsync(IRenderedComponent<MudContributorPicker> cut, AuthorRole role)
    {
        var select = cut.FindComponent<MudSelect<AuthorRole>>();
        await cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(role));
    }

    private static async Task ClickAddAsync(IRenderedComponent<MudContributorPicker> cut)
    {
        var add = cut.FindComponents<MudButton>().Single(b => b.Markup.Contains("Add"));
        await cut.InvokeAsync(() => add.Instance.OnClick.InvokeAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
    }

    [Fact]
    public void EmptyContributors_RendersNoChips()
    {
        var cut = RenderPicker([]);

        Assert.Empty(cut.FindAll(".mud-chip"));
    }

    [Fact]
    public void PreSeededContributors_RendersNameAndRoleChips()
    {
        var cut = RenderPicker(
        [
            new ContributorEntry("Doug Mauss", AuthorRole.Editor),
            new ContributorEntry("Edith Grossman", AuthorRole.Translator),
        ]);

        var chips = cut.FindAll(".mud-chip");
        Assert.Equal(2, chips.Count);
        Assert.Contains(chips, c => c.TextContent.Contains("Doug Mauss (editor)"));
        Assert.Contains(chips, c => c.TextContent.Contains("Edith Grossman (translator)"));
    }

    [Fact]
    public void AddButton_DisabledUntilANameIsTyped()
    {
        var cut = RenderPicker([]);

        var add = cut.FindComponents<MudButton>().Single(b => b.Markup.Contains("Add"));
        Assert.True(add.Instance.Disabled);
    }

    [Fact]
    public async Task Add_TypedName_AddsChipWithDefaultEditorRole()
    {
        var contributors = new List<ContributorEntry>();
        List<ContributorEntry>? captured = null;
        var cut = RenderPicker(contributors, list => captured = list);

        await TypeNameAsync(cut, "  Doug Mauss  ");
        await ClickAddAsync(cut);

        var entry = Assert.Single(contributors);
        Assert.Equal("Doug Mauss", entry.Name);
        Assert.Equal(AuthorRole.Editor, entry.Role);
        Assert.NotNull(captured);
        Assert.Same(contributors, captured);
    }

    [Fact]
    public async Task Add_PickedRole_IsCarriedOnTheChip()
    {
        var contributors = new List<ContributorEntry>();
        var cut = RenderPicker(contributors);

        await TypeNameAsync(cut, "Edith Grossman");
        await PickRoleAsync(cut, AuthorRole.Translator);
        await ClickAddAsync(cut);

        var entry = Assert.Single(contributors);
        Assert.Equal("Edith Grossman", entry.Name);
        Assert.Equal(AuthorRole.Translator, entry.Role);
    }

    [Fact]
    public async Task Add_NewName_EagerCreatesViaDispatcher()
    {
        var cut = RenderPicker([]);

        await TypeNameAsync(cut, "Doug Mauss");
        await ClickAddAsync(cut);

        await _dispatcher.Received(1).Send(
            Arg.Is<CreateAuthor>(c => c!.Name == "Doug Mauss"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Add_SameNameAndRole_CaseInsensitive_IsNoOp()
    {
        var contributors = new List<ContributorEntry> { new("Doug Mauss", AuthorRole.Editor) };
        List<ContributorEntry>? captured = null;
        var cut = RenderPicker(contributors, list => captured = list);

        await TypeNameAsync(cut, "DOUG MAUSS");
        await ClickAddAsync(cut);

        Assert.Single(contributors);
        Assert.Null(captured);
        await _dispatcher.DidNotReceive().Send(
            Arg.Any<CreateAuthor>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Add_SameNameDifferentRole_AddsSecondChip()
    {
        var contributors = new List<ContributorEntry> { new("Doug Mauss", AuthorRole.Editor) };
        var cut = RenderPicker(contributors);

        await TypeNameAsync(cut, "Doug Mauss");
        await PickRoleAsync(cut, AuthorRole.Compiler);
        await ClickAddAsync(cut);

        Assert.Equal(2, contributors.Count);
        Assert.Contains(contributors, c => c.Role == AuthorRole.Compiler);
    }

    [Fact]
    public async Task Add_EagerCreateThrows_StillAddsChip()
    {
        _dispatcher.Send(Arg.Any<CreateAuthor>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("transient")));
        var contributors = new List<ContributorEntry>();
        var cut = RenderPicker(contributors);

        await TypeNameAsync(cut, "Doug Mauss");
        await ClickAddAsync(cut);

        Assert.Single(contributors);
    }

    [Fact]
    public async Task Search_QueriesAuthorNames_WithNothingExcluded()
    {
        // Unlike the author picker, a name already chipped stays suggestable: one
        // person can hold several roles on a Work.
        _dispatcher.Query(Arg.Any<SearchAuthorNames>(), Arg.Any<CancellationToken>())
            .Returns(["Doug Mauss"]);
        var cut = RenderPicker([new ContributorEntry("Doug Mauss", AuthorRole.Editor)]);

        var results = await cut.Instance.SearchAsync("doug", CancellationToken.None);

        Assert.Equal(["Doug Mauss"], results);
        await _dispatcher.Received(1).Query(
            Arg.Is<SearchAuthorNames>(q => q!.Query == "doug" && q.Exclude.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChipClose_RemovesThatContributor()
    {
        var contributors = new List<ContributorEntry>
        {
            new("Doug Mauss", AuthorRole.Editor),
            new("Edith Grossman", AuthorRole.Translator),
        };
        List<ContributorEntry>? captured = null;
        var cut = RenderPicker(contributors, list => captured = list);

        var chip = cut.FindComponents<MudChip<string>>()
            .First(c => c.Instance.Text == "Doug Mauss (editor)");
        await cut.InvokeAsync(() => chip.Instance.OnClose.InvokeAsync(chip.Instance));

        var remaining = Assert.Single(contributors);
        Assert.Equal("Edith Grossman", remaining.Name);
        Assert.NotNull(captured);
    }
}
