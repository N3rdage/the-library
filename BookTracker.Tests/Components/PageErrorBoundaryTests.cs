using BookTracker.Web.Components.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BookTracker.Tests.Components;

// The page-level boundary: a child that throws while rendering is replaced by
// the error content (with a trace id), and Recover puts the child back.
[Trait("Category", TestCategories.Component)]
public class PageErrorBoundaryTests : ComponentTestBase
{
    private sealed class FlakyChild : ComponentBase
    {
        public static bool Throw;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Throw) throw new InvalidOperationException("render failed");
            builder.AddMarkupContent(0, "<p id=\"child\">child ok</p>");
        }
    }

    private IRenderedComponent<PageErrorBoundary> RenderBoundary() =>
        Render<PageErrorBoundary>(p => p
            .AddChildContent<FlakyChild>()
            .Add(b => b.ErrorContent, (RenderFragment<Exception>)(ex => b => b.AddContent(0, $"error: {ex.Message}"))));

    [Fact]
    public void ThrowingChild_showsErrorContent_withTraceId()
    {
        FlakyChild.Throw = true;

        var cut = RenderBoundary();

        cut.WaitForAssertion(() => Assert.Contains("error: render failed", cut.Markup));
        Assert.Empty(cut.FindAll("#child"));
        Assert.False(string.IsNullOrEmpty(cut.Instance.TraceId));
    }

    [Fact]
    public void Recover_rendersTheChildAgain()
    {
        FlakyChild.Throw = true;
        var cut = RenderBoundary();
        cut.WaitForAssertion(() => Assert.Contains("error: render failed", cut.Markup));

        FlakyChild.Throw = false;
        cut.InvokeAsync(() => cut.Instance.Recover());

        cut.WaitForAssertion(() => Assert.Equal("child ok", cut.Find("#child").TextContent));
        Assert.DoesNotContain("error:", cut.Markup);
    }
}
