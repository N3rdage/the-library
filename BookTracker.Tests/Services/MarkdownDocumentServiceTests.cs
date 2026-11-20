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
    public void PipeTables_render()
    {
        var doc = ServiceWith("| a | b |\n|---|---|\n| 1 | 2 |\n").Get("user-guide");

        Assert.NotNull(doc);
        Assert.Contains("<table>", doc.Html);
    }
}
