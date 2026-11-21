using System.Net;
using BookTracker.Application;
using BookTracker.Web;
using BookTracker.Web.Api;
using BookTracker.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BookTracker.Tests.Api;

// The real rate-limiter registration and the real route mapping behind a
// TestServer, with the providers and the dispatcher stubbed. Every request
// here is a 400 (a malformed ISBN or a blank author), so no stub is called;
// the limiter counts it all the same.
[Trait("Category", TestCategories.Unit)]
public class OnlineRateLimitTests
{
    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRateLimiter(ProgramSetup.ConfigureRateLimiter);
        builder.Services.AddSingleton(Substitute.For<IBookLookupService>());
        builder.Services.AddSingleton(Substitute.For<IDispatcher>());

        var app = builder.Build();
        app.UseRateLimiter();
        app.MapOnlineEndpoints();
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task RequestsBeyondThePermitLimit_areRejectedWith429()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        for (var i = 0; i < OnlineEndpoints.PermitLimit; i++)
        {
            var allowed = await client.GetAsync("/api/online/isbn/123");
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        var rejected = await client.GetAsync("/api/online/isbn/123");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task TheThreeRoutes_shareOneWindow()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        for (var i = 0; i < OnlineEndpoints.PermitLimit; i++)
            await client.GetAsync("/api/online/isbn/123");

        var authorWorks = await client.GetAsync("/api/online/author-works");
        var workEditions = await client.GetAsync("/api/online/work-editions");

        Assert.Equal(HttpStatusCode.TooManyRequests, authorWorks.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, workEditions.StatusCode);
    }
}
