using System.Text.RegularExpressions;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

[Trait("Category", TestCategories.Unit)]
public sealed class MarkdownDocumentServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public MarkdownDocumentServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "docs"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private MarkdownDocumentService ServiceWith(string markdown)
    {
        File.WriteAllText(Path.Combine(root, "docs", "USER-GUIDE.md"), markdown);
        return new MarkdownDocumentService([root]);
    }

    [Theory]
    [InlineData("secrets")]
    [InlineData("../appsettings")]
    [InlineData("USER-GUIDE.md")]
    [InlineData("")]
    public void UnknownName_isRefused(string name)
    {
        Assert.Null(ServiceWith("# Title").Get(name));
    }

    [Fact]
    public void RawHtml_isEscaped_notEmitted()
    {
        var doc = ServiceWith("# Title\n\n<script>alert(1)</script>\n\nsome <b>bold</b> text").Get("user-guide");

        Assert.NotNull(doc);
        Assert.DoesNotContain("<script", doc.Html);
        Assert.DoesNotContain("<b>", doc.Html);
        Assert.Contains("&lt;script&gt;", doc.Html);
    }

    [Fact]
    public void Headings_getIds_andLevelTwoOnesFormTheToc()
    {
        var doc = ServiceWith("# Title\n\n## First part\n\ntext\n\n### Sub\n\n## Second `part`\n").Get("user-guide");

        Assert.NotNull(doc);
        Assert.Equal(
            [new MarkdownHeading("First part", "first-part"), new MarkdownHeading("Second part", "second-part")],
            doc.Headings);
        Assert.Contains("id=\"first-part\"", doc.Html);
        Assert.Contains("id=\"second-part\"", doc.Html);
    }

    [Fact]
    public void RenderedDocument_isCachedPerProcess()
    {
        var service = ServiceWith("# One");
        var first = service.Get("user-guide");

        File.WriteAllText(Path.Combine(root, "docs", "USER-GUIDE.md"), "# Two");

        Assert.Same(first, service.Get("user-guide"));
    }

    [Fact]
    public void AnchorLinks_carryTheDocumentsRoute_otherLinksDoNot()
    {
        var doc = ServiceWith("See [Duplicates](#duplicates) and [site](https://example.org/#x).").Get("user-guide");

        Assert.NotNull(doc);
        Assert.Contains("href=\"manual#duplicates\"", doc.Html);
        Assert.Contains("href=\"https://example.org/#x\"", doc.Html);
    }

    [Fact]
    public void Headings_getGitHubStyleIds()
    {
        var doc = ServiceWith("## Scope & intent\n\n[s](#scope--intent)\n").Get("user-guide");

        Assert.NotNull(doc);
        Assert.Contains("id=\"scope--intent\"", doc.Html);
    }

    [Fact]
    public void DocumentWithItsOwnContentsSection_getsNoSecondContentsList()
    {
        var doc = ServiceWith("# Title\n\n## Contents\n\n1. [Palette](#palette)\n\n## Palette\n").Get("user-guide");

        Assert.NotNull(doc);
        Assert.Empty(doc.Headings);
    }

    [Theory]
    [InlineData("user-guide", "manual")]
    [InlineData("style-guide", "style")]
    public void ShippedDocuments_everyAnchorLinkNamesAHeadingId(string name, string route)
    {
        // The shipped files, as copied beside the binaries.
        var doc = new MarkdownDocumentService([AppContext.BaseDirectory]).Get(name);

        Assert.NotNull(doc);
        var ids = Regex.Matches(doc.Html, "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        var anchors = Regex.Matches(doc.Html, $"href=\"{route}#([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.All(anchors, anchor => Assert.Contains(anchor, ids));
        Assert.All(doc.Headings, heading => Assert.Contains(heading.Id, ids));
    }

    [Fact]
    public void PipeTables_render()
    {
        var doc = ServiceWith("| a | b |\n|---|---|\n| 1 | 2 |\n").Get("user-guide");

        Assert.NotNull(doc);
        Assert.Contains("<table>", doc.Html);
    }
}
