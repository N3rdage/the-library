using System.Diagnostics;
using System.Security.Claims;
using BookTracker.Web.Telemetry;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace BookTracker.Tests.Telemetry;

[Trait("Category", TestCategories.Unit)]
public class UserActivityProcessorTests
{
    [Fact]
    public void OnEnd_WithAuthenticatedUser_SetsEndUserIdTag()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "user@example.com")],
                authenticationType: "TestAuth"))
        };
        accessor.HttpContext.Returns(ctx);

        var processor = new UserActivityProcessor(accessor);
        var activity = new Activity("test");
        processor.OnEnd(activity);

        Assert.Equal("user@example.com", activity.GetTagItem("enduser.id"));
    }

    [Fact]
    public void OnEnd_WithAnonymousUser_LeavesEndUserIdTagUnset()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        // Anonymous: no authentication type → Identity.Name is null.
        accessor.HttpContext.Returns(new DefaultHttpContext());

        var processor = new UserActivityProcessor(accessor);
        var activity = new Activity("test");
        processor.OnEnd(activity);

        Assert.Null(activity.GetTagItem("enduser.id"));
    }

    [Fact]
    public void OnEnd_WithNullHttpContext_DoesNotThrow()
    {
        // Background services / startup tasks emit activities outside any
        // request — HttpContextAccessor returns null then. The processor
        // must no-op rather than NRE the export pipeline.
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);

        var processor = new UserActivityProcessor(accessor);
        var activity = new Activity("test");
        var ex = Record.Exception(() => processor.OnEnd(activity));

        Assert.Null(ex);
        Assert.Null(activity.GetTagItem("enduser.id"));
    }
}
