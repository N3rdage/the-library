using BookTracker.Web.Components.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BookTracker.Tests.Components;

// The page-level boundary: a child that throws while rendering is replaced by
// the error content (with a trace id), and Recover puts the child back. The
// last test renders it the way MainLayout uses it.
[Trait("Category", TestCategories.Component)]
public class PageErrorBoundaryTests : ComponentTestBase
{
    private sealed class FlakyChild : ComponentBase
    {
        [Parameter] public bool Throw { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Throw) throw new InvalidOperationException("render failed");
            builder.AddMarkupContent(0, "<p id=\"child\">child ok</p>");
        }
    }

    private static RenderFragment Flaky(Func<bool> shouldThrow) => builder =>
    {
        builder.OpenComponent<FlakyChild>(0);
        builder.AddComponentParameter(1, nameof(FlakyChild.Throw), shouldThrow());
        builder.CloseComponent();
    };

    private IRenderedComponent<PageErrorBoundary> RenderBoundary(Func<bool> shouldThrow) =>
        Render<PageErrorBoundary>(p => p
            .Add(b => b.ChildContent, Flaky(shouldThrow))
            .Add(b => b.ErrorContent, (RenderFragment<Exception>)(ex => b => b.AddContent(0, $"error: {ex.Message}"))));

    [Fact]
    public void ThrowingChild_showsErrorContent_withTraceId()
    {
        var cut = RenderBoundary(() => true);

        cut.WaitForAssertion(() => Assert.Contains("error: render failed", cut.Markup));
        Assert.Empty(cut.FindAll("#child"));
        Assert.False(string.IsNullOrEmpty(cut.Instance.TraceId));
    }

    [Fact]
    public void Recover_rendersTheChildAgain()
    {
        var throwing = true;
        var cut = RenderBoundary(() => throwing);
        cut.WaitForAssertion(() => Assert.Contains("error: render failed", cut.Markup));

        throwing = false;
        cut.InvokeAsync(() => cut.Instance.Recover());

        cut.WaitForAssertion(() => Assert.Equal("child ok", cut.Find("#child").TextContent));
        Assert.DoesNotContain("error:", cut.Markup);
    }

    [Fact]
    public void MainLayout_aFaultInTheBody_showsTheMappedError_withTraceId_andRecover()
    {
        var throwing = true;

        var cut = Render<MainLayout>(p => p.Add(l => l.Body, Flaky(() => throwing)));

        cut.WaitForAssertion(() => Assert.Contains("Trace ID:", cut.Markup));
        Assert.Empty(cut.FindAll("#child"));
        Assert.Contains("Bookcase", cut.Markup);

        throwing = false;
        cut.Find("main button.btn-primary").Click();

        cut.WaitForAssertion(() => Assert.Equal("child ok", cut.Find("#child").TextContent));
        Assert.DoesNotContain("Trace ID:", cut.Markup);
    }
}
