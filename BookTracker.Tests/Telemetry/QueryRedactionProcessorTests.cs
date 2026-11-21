using System.Diagnostics;
using BookTracker.Web.Telemetry;

namespace BookTracker.Tests.Telemetry;

[Trait("Category", TestCategories.Unit)]
public class QueryRedactionProcessorTests
{
    // Activity.Kind can only be set by ActivitySource.StartActivity, which
    // returns null unless a listener samples the source.
    private static readonly ActivitySource Source = CreateSampledSource();

    private static ActivitySource CreateSampledSource()
    {
        var source = new ActivitySource(nameof(QueryRedactionProcessorTests));
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = s => s == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        });
        return source;
    }

    private static Activity Process(ActivityKind kind, string tag, string value)
    {
        var activity = Source.StartActivity("test", kind)!;
        activity.SetTag(tag, value);
        new QueryRedactionProcessor().OnEnd(activity);
        return activity;
    }

    [Fact]
    public void OnEnd_RequestQuery_ValueIsRedactedAndNameKept()
    {
        using var activity = Process(ActivityKind.Server, "url.query", "?borrower=Jane%20Doe");

        Assert.Equal("?borrower=Redacted", activity.GetTagItem("url.query"));
        Assert.DoesNotContain(activity.TagObjects, t => t.Value is string s && s.Contains("Jane"));
    }

    [Fact]
    public void OnEnd_SinceIsKeptAndOtherValuesRedacted()
    {
        using var activity = Process(ActivityKind.Server, "url.query", "?since=2026-01-01T00:00:00Z&x=1");

        Assert.Equal("?since=2026-01-01T00:00:00Z&x=Redacted", activity.GetTagItem("url.query"));
    }

    [Fact]
    public void OnEnd_DependencyUrl_RedactsKeyAndSearchTextButKeepsHostAndPath()
    {
        using var activity = Process(
            ActivityKind.Client, "url.full", "https://api.trove.nla.gov.au/v3/result?q=dune&key=SECRET");

        var url = Assert.IsType<string>(activity.GetTagItem("url.full"));
        Assert.Equal("https://api.trove.nla.gov.au/v3/result?q=Redacted&key=Redacted", url);
        Assert.DoesNotContain("SECRET", url);
        Assert.DoesNotContain("dune", url);
    }

    [Fact]
    public void OnEnd_RequestTarget_QueryIsRedacted()
    {
        using var activity = Process(ActivityKind.Server, "http.target", "/api/online/author-works?author=King");

        Assert.Equal("/api/online/author-works?author=Redacted", activity.GetTagItem("http.target"));
    }

    [Fact]
    public void OnEnd_NoQuery_TagsAreUntouched()
    {
        using var activity = Process(ActivityKind.Client, "url.full", "https://example.com/a/b");
        activity.SetTag("url.path", "/a/b");
        new QueryRedactionProcessor().OnEnd(activity);

        Assert.Equal("https://example.com/a/b", activity.GetTagItem("url.full"));
        Assert.Equal("/a/b", activity.GetTagItem("url.path"));
    }

    [Fact]
    public void OnEnd_ParameterWithoutValue_DoesNotThrowAndIsKept()
    {
        Activity? activity = null;
        var ex = Record.Exception(() => activity = Process(ActivityKind.Server, "url.query", "?flag&x=1"));

        Assert.Null(ex);
        Assert.Equal("?flag&x=Redacted", activity!.GetTagItem("url.query"));
        activity.Dispose();
    }
}
