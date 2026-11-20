using BookTracker.Web.Components.Pages.Manual;
using BookTracker.Web.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BookTracker.Tests.Components;

[Trait("Category", TestCategories.Component)]
public class ManualPageTests : ComponentTestBase
{
    private static MarkdownDocument Fixture(string name) => new(
        name,
        "<h1 id=\"title\">Title</h1><h2 id=\"alpha\">Alpha</h2><p>one</p><h2 id=\"beta\">Beta</h2>",
        [new MarkdownHeading("Alpha", "alpha"), new MarkdownHeading("Beta", "beta")]);

    private IRenderedComponent<Document> RenderAt(string path)
    {
        var service = Substitute.For<IMarkdownDocumentService>();
        service.Get("user-guide").Returns(Fixture("user-guide"));
        service.Get("style-guide").Returns(Fixture("style-guide"));
        Services.AddSingleton(service);

        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo(path);
        return Render<Document>();
    }

    [Fact]
    public void Manual_rendersHeadings_andContentsLinks()
    {
        var cut = RenderAt("/manual");

        Assert.Equal("Alpha", cut.Find("article h2#alpha").TextContent);
        Assert.Equal("Beta", cut.Find("article h2#beta").TextContent);
        var links = cut.FindAll("#document-toc a");
        // The app has <base href="/">, so a bare "#alpha" would resolve to the home page.
        Assert.Equal(["manual#alpha", "manual#beta"], links.Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void Style_servesTheStyleGuide()
    {
        var service = Substitute.For<IMarkdownDocumentService>();
        service.Get("style-guide").Returns(Fixture("style-guide"));
        Services.AddSingleton(service);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/style");

        var cut = Render<Document>();

        Assert.NotEmpty(cut.FindAll("article h2#alpha"));
        Assert.Equal("style#alpha", cut.Find("#document-toc a").GetAttribute("href"));
        service.Received().Get("style-guide");
        service.DidNotReceive().Get("user-guide");
    }

    [Fact]
    public void MissingDocument_showsNotFound()
    {
        var service = Substitute.For<IMarkdownDocumentService>();
        service.Get(Arg.Any<string>()).Returns((MarkdownDocument?)null);
        Services.AddSingleton(service);

        var cut = Render<Document>();

        Assert.Contains("does not exist", cut.Find("#document-not-found").TextContent);
        Assert.Empty(cut.FindAll("article"));
    }
}
