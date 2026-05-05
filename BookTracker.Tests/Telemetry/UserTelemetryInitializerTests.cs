using System.Security.Claims;
using BookTracker.Web.Telemetry;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace BookTracker.Tests.Telemetry;

public class UserTelemetryInitializerTests
{
    [Fact]
    public void Initialize_WithAuthenticatedUser_SetsAuthenticatedUserId()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "drew@silly.ninja")],
                authenticationType: "TestAuth"))
        };
        accessor.HttpContext.Returns(ctx);

        var initializer = new UserTelemetryInitializer(accessor);
        var telemetry = new TraceTelemetry();
        initializer.Initialize(telemetry);

        Assert.Equal("drew@silly.ninja", telemetry.Context.User.AuthenticatedUserId);
    }

    [Fact]
    public void Initialize_WithAnonymousUser_LeavesAuthenticatedUserIdNull()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        // Anonymous: no authentication type → Identity.Name is null.
        accessor.HttpContext.Returns(new DefaultHttpContext());

        var initializer = new UserTelemetryInitializer(accessor);
        var telemetry = new TraceTelemetry();
        initializer.Initialize(telemetry);

        Assert.Null(telemetry.Context.User.AuthenticatedUserId);
    }

    [Fact]
    public void Initialize_WithNullHttpContext_DoesNotThrow()
    {
        // Background services / startup tasks invoke logging outside any
        // request — HttpContextAccessor returns null then. The initializer
        // must no-op rather than NRE the whole request pipeline.
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);

        var initializer = new UserTelemetryInitializer(accessor);
        var telemetry = new TraceTelemetry();
        var ex = Record.Exception(() => initializer.Initialize(telemetry));

        Assert.Null(ex);
        Assert.Null(telemetry.Context.User.AuthenticatedUserId);
    }
}
