using System.Collections.Concurrent;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BookTracker.Web.Services;

/// <summary>One <c>##</c> heading: its plain text and the anchor id Markdig generated.</summary>
public sealed record MarkdownHeading(string Text, string Id);

/// <summary>
/// A rendered markdown document plus its table of contents: the <c>##</c>
/// headings, or none when the document has its own <c>## Contents</c> section.
/// </summary>
public sealed record MarkdownDocument(string Name, string Html, IReadOnlyList<MarkdownHeading> Headings);

/// <summary>
/// Renders the shipped markdown docs (user guide, style guide) for in-app
/// pages. Only a fixed allow-list of names is served; the name is never used
/// as a path.
/// </summary>
public interface IMarkdownDocumentService
{
    /// <summary>The rendered document, or null when <paramref name="name"/> is not on the allow-list.</summary>
    MarkdownDocument? Get(string name);
}

public sealed class MarkdownDocumentService : IMarkdownDocumentService
{
    // Name -> (file under docs\, the route the document is served at).
    private static readonly IReadOnlyDictionary<string, (string FileName, string Route)> Documents =
        new Dictionary<string, (string FileName, string Route)>(StringComparer.Ordinal)
        {
            ["user-guide"] = ("USER-GUIDE.md", "manual"),
            ["style-guide"] = ("STYLE-GUIDE.md", "style"),
        };

    // Raw HTML in the source is escaped, not emitted; only pipe tables and
    // heading anchors are switched on. GitHub-style ids, so the documents' own
    // links ("#scope--intent") work in the app as they do on GitHub.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .Build();

    private readonly string[] roots;
    private readonly ConcurrentDictionary<string, MarkdownDocument> cache = new(StringComparer.Ordinal);

    // The linked docs are copied next to the binaries (AppContext.BaseDirectory)
    // in dev, test and publish runs; the content root covers a host whose
    // content root is the publish folder.
    public MarkdownDocumentService(IWebHostEnvironment environment)
        : this([AppContext.BaseDirectory, environment.ContentRootPath])
    {
    }

    internal MarkdownDocumentService(string[] roots) => this.roots = roots;

    public MarkdownDocument? Get(string name)
    {
        if (!Documents.TryGetValue(name, out var document))
        {
            return null;
        }
        return cache.GetOrAdd(name, _ => Load(name, document.FileName, document.Route));
    }

    private MarkdownDocument Load(string name, string fileName, string route)
    {
        var path = roots
            .Select(root => Path.Combine(root, "docs", fileName))
            .FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Document '{fileName}' was not found under docs\\ in any of: {string.Join(", ", roots)}");

        var ast = Markdown.Parse(File.ReadAllText(path), Pipeline);

        foreach (var link in ast.Descendants<LinkInline>())
        {
            if (link.Url is not { } url)
            {
                continue;
            }

            if (!IsAllowedUrl(url))
            {
                link.Url = "#";
            }
            else if (url.StartsWith('#'))
            {
                // The app has <base href="/">, so a bare "#anchor" link would
                // resolve to the home page; put the document's route in front of it.
                link.Url = route + url;
            }
        }

        // A "<scheme:...>" autolink has no separate text, so a disallowed one
        // becomes the plain text it would have shown.
        foreach (var autolink in ast.Descendants<AutolinkInline>().ToList())
        {
            if (!IsAllowedUrl(autolink.Url))
            {
                autolink.ReplaceBy(new LiteralInline(autolink.Url));
            }
        }

        var headings = ast.OfType<HeadingBlock>()
            .Where(h => h.Level == 2 && h.GetAttributes().Id is not null)
            .Select(h => new MarkdownHeading(PlainText(h), h.GetAttributes().Id!))
            .ToList();

        // A document that carries its own "## Contents" section needs no second
        // list from the page, so it gets an empty one.
        if (headings.Any(h => string.Equals(h.Text.Trim(), "Contents", StringComparison.OrdinalIgnoreCase)))
        {
            headings = [];
        }

        return new MarkdownDocument(name, ast.ToHtml(Pipeline), headings);
    }

    // Markdig does not filter link schemes. A URL is allowed when it is relative
    // or its scheme is http, https or mailto. Browsers ignore ASCII whitespace
    // and control characters inside a scheme ("java\tscript:"), so those are
    // dropped before looking for a ':' ahead of any '/', '?' or '#'.
    private static bool IsAllowedUrl(string url)
    {
        var compact = string.Concat(url.Where(c => c > ' ' && c != '\u007f'));
        var colon = compact.IndexOfAny([':', '/', '?', '#']);
        if (colon < 0 || compact[colon] != ':')
        {
            return true;
        }

        var scheme = compact[..colon];
        return scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase);
    }

    private static string PlainText(HeadingBlock heading) =>
        heading.Inline is null
            ? string.Empty
            : string.Concat(heading.Inline.Descendants().Select(i => i switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                _ => string.Empty,
            }));
}
