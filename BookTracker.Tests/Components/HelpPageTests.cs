using System.Text.RegularExpressions;
using HelpIndex = BookTracker.Web.Components.Pages.Help.Index;
using BookTracker.Web.Services;
using Bunit;

namespace BookTracker.Tests.Components;

[Trait("Category", TestCategories.Component)]
public class HelpPageTests : ComponentTestBase
{
    private static readonly string[] SurfaceHrefs =
    [
        "", "books", "books/add", "books/bulk-add", "series", "shelf-order", "authors",
        "publishers", "wishlist", "loans", "bookshop", "assistant", "duplicates", "privacy", "manual",
    ];

    [Fact]
    public void Help_linksEveryNavSurface_inNavOrder()
    {
        var cut = Render<HelpIndex>();

        var hrefs = cut.FindAll("#help-surfaces tr").Select(r => r.QuerySelector("td a")!.GetAttribute("href"));
        Assert.Equal(SurfaceHrefs, hrefs);
    }

    [Fact]
    public void Help_manualLinks_areNeverBare()
    {
        var cut = Render<HelpIndex>();

        var manual = cut.FindAll("a").Select(a => a.GetAttribute("href")!)
            .Where(h => h.Contains('#')).ToList();
        Assert.NotEmpty(manual);
        Assert.All(manual, h => Assert.Matches(@"^manual#[a-z0-9-]+$", h));
        Assert.Contains("manual#key-concepts", manual);
    }

    [Fact]
    public void Help_manualLinks_resolveToHeadingsInTheShippedGuide()
    {
        var doc = new MarkdownDocumentService([AppContext.BaseDirectory]).Get("user-guide");
        Assert.NotNull(doc);
        var ids = Regex.Matches(doc.Html, "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

        var cut = Render<HelpIndex>();
        var targets = cut.FindAll("a").Select(a => a.GetAttribute("href")!)
            .Where(h => h.StartsWith("manual#")).Select(h => h["manual#".Length..]).Distinct();

        foreach (var id in targets)
            Assert.True(ids.Contains(id), $"USER-GUIDE.md has no heading with id '{id}'");
    }
}
