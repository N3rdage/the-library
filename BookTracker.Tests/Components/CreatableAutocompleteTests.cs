using BookTracker.Web.Components.Shared;
using Bunit;
using Xunit;

namespace BookTracker.Tests.Components;

/// <summary>
/// bUnit tests for the shared CreatableAutocomplete gesture (the single-select
/// "pick existing or explicitly Add" typeahead used by the series + publisher
/// fields). Covers the two regression-prone bits: when the synthetic "Add …" row
/// is offered, and that selecting it commits the TYPED name (never the raw
/// sentinel — a broken strip would otherwise save the marker string as a series
/// / publisher name).
/// </summary>
public class CreatableAutocompleteTests : ComponentTestBase
{
    private IRenderedComponent<CreatableAutocomplete> Render(
        Func<string?, CancellationToken, Task<IEnumerable<string>>> search,
        Action<string?> onValueChanged)
        => Render<CreatableAutocomplete>(p => p
            .Add(c => c.SearchExisting, search)
            .Add(c => c.ValueChanged, onValueChanged));

    private static Func<string?, CancellationToken, Task<IEnumerable<string>>> Existing(params string[] names)
        => (q, _) => Task.FromResult<IEnumerable<string>>(
            string.IsNullOrWhiteSpace(q)
                ? names
                : names.Where(n => n.Contains(q!.Trim(), StringComparison.OrdinalIgnoreCase)));

    [Fact]
    public async Task Search_NoExactMatch_AppendsAddRow()
    {
        var cut = Render(Existing("Discworld"), _ => { });

        var results = (await cut.Instance.SearchAsync("Disc", default)).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains("Discworld", results);
        Assert.Contains(CreatableAutocomplete.AddMarker, results); // the explicit "Add ..." row
    }

    [Fact]
    public async Task Search_ExactMatch_DoesNotAppendAddRow()
    {
        // Case-insensitive: an exact existing name means "pick it", not "add".
        var cut = Render(Existing("Discworld"), _ => { });

        var results = (await cut.Instance.SearchAsync("discworld", default)).ToList();

        Assert.Equal(["Discworld"], results);
        Assert.DoesNotContain(CreatableAutocomplete.AddMarker, results);
    }

    [Fact]
    public async Task Search_BlankQuery_DoesNotAppendAddRow()
    {
        var cut = Render(Existing("Discworld"), _ => { });

        var results = (await cut.Instance.SearchAsync("   ", default)).ToList();

        Assert.DoesNotContain(CreatableAutocomplete.AddMarker, results);
    }

    [Fact]
    public async Task Select_AddRow_CommitsTypedNameNotTheMarker()
    {
        string? committed = "sentinel-unset";
        var cut = Render(Existing("Discworld"), v => committed = v);

        await cut.InvokeAsync(() => cut.Instance.SearchAsync("Mistborn", default)); // sets the pending query
        await cut.InvokeAsync(() => cut.Instance.OnSelectedAsync(CreatableAutocomplete.AddMarker));

        Assert.Equal("Mistborn", committed); // NOT the raw marker
    }

    [Fact]
    public async Task Select_ExistingName_CommitsThatName()
    {
        string? committed = null;
        var cut = Render(Existing("Discworld"), v => committed = v);

        await cut.InvokeAsync(() => cut.Instance.OnSelectedAsync("Discworld"));

        Assert.Equal("Discworld", committed);
    }

    [Fact]
    public async Task Select_Null_CommitsNull()
    {
        // The Clearable X commits a null selection.
        string? committed = "sentinel-unset";
        var cut = Render(Existing("Discworld"), v => committed = v);

        await cut.InvokeAsync(() => cut.Instance.OnSelectedAsync(null));

        Assert.Null(committed);
    }
}
