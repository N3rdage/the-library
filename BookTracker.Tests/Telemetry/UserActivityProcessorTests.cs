using System.Diagnostics;
using System.Security.Claims;
using BookTracker.Web.Telemetry;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace BookTracker.Tests.Telemetry;

[Trait("Category", TestCategories.Unit)]
public class UserActivityProcessorTests
{
    private static UserActivityProcessor ProcessorFor(HttpContext? context)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        return new UserActivityProcessor(accessor);
    }

    [Fact]
    public void EndUserIdTag_IsTheKeyTheAzureMonitorExporterMapsToAuthenticatedUserId()
    {
        // The Azure Monitor exporter's ActivityTagsProcessor treats the
        // OpenTelemetry semantic-convention key "enduser.id" as the end user
        // and writes it to the ai.user.authUserId context tag.
        Assert.Equal("enduser.id", UserActivityProcessor.EndUserIdTag);
    }

    [Fact]
    public void OnEnd_WithEasyAuthHeader_SetsEndUserIdTag()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[UserActivityProcessor.PrincipalNameHeader] = "user@example.com";

        var activity = new Activity("test");
        ProcessorFor(ctx).OnEnd(activity);

        Assert.Equal("user@example.com", activity.GetTagItem(UserActivityProcessor.EndUserIdTag));
    }

    [Fact]
    public void OnEnd_HeaderWinsOverIdentityName()
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "identity@example.com")],
                authenticationType: "TestAuth"))
        };
        ctx.Request.Headers[UserActivityProcessor.PrincipalNameHeader] = "header@example.com";

        var activity = new Activity("test");
        ProcessorFor(ctx).OnEnd(activity);

        Assert.Equal("header@example.com", activity.GetTagItem(UserActivityProcessor.EndUserIdTag));
    }

    [Fact]
    public void OnEnd_WithoutHeaderButAuthenticatedUser_SetsEndUserIdTag()
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "user@example.com")],
                authenticationType: "TestAuth"))
        };

        var activity = new Activity("test");
        ProcessorFor(ctx).OnEnd(activity);

        Assert.Equal("user@example.com", activity.GetTagItem(UserActivityProcessor.EndUserIdTag));
    }

    [Fact]
    public void OnEnd_WithNoHeaderAndAnonymousUser_LeavesEndUserIdTagUnset()
    {
        var activity = new Activity("test");
        ProcessorFor(new DefaultHttpContext()).OnEnd(activity);

        Assert.Null(activity.GetTagItem(UserActivityProcessor.EndUserIdTag));
    }

    [Fact]
    public void OnEnd_WithNullHttpContext_DoesNotThrow()
    {
        // Background services / startup tasks emit activities outside any
        // request — HttpContextAccessor returns null then. The processor
        // must no-op rather than NRE the export pipeline.
        var activity = new Activity("test");
        var ex = Record.Exception(() => ProcessorFor(null).OnEnd(activity));

        Assert.Null(ex);
        Assert.Null(activity.GetTagItem(UserActivityProcessor.EndUserIdTag));
    }

    [Fact]
    public void OnEnd_ServerActivityWithoutServerAddress_TakesHostAndSchemeFromRequest()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("books-staging.example.com", 443);

        var activity = new Activity("test");
        activity.SetKind(ActivityKind.Server);
        ProcessorFor(ctx).OnEnd(activity);

        Assert.Equal("books-staging.example.com", activity.GetTagItem(UserActivityProcessor.ServerAddressTag));
        Assert.Equal("https", activity.GetTagItem(UserActivityProcessor.UrlSchemeTag));
    }

    [Fact]
    public void OnEnd_ServerActivityWithServerAddress_KeepsIt()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("other.example.com");

        var activity = new Activity("test");
        activity.SetKind(ActivityKind.Server);
        activity.SetTag(UserActivityProcessor.ServerAddressTag, "set.example.com");
        ProcessorFor(ctx).OnEnd(activity);

        Assert.Equal("set.example.com", activity.GetTagItem(UserActivityProcessor.ServerAddressTag));
    }

    [Fact]
    public void OnEnd_ClientActivity_HostTagsUntouched()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("books-staging.example.com");

        var activity = new Activity("test");
        activity.SetKind(ActivityKind.Client);
        ProcessorFor(ctx).OnEnd(activity);

        Assert.Null(activity.GetTagItem(UserActivityProcessor.ServerAddressTag));
        Assert.Null(activity.GetTagItem(UserActivityProcessor.UrlSchemeTag));
    }
}
