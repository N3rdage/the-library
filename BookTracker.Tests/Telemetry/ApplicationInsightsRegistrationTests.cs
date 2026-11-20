using BookTracker.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace BookTracker.Tests.Telemetry;

[Trait("Category", TestCategories.Unit)]
public class ApplicationInsightsRegistrationTests
{
    [Fact]
    public void WithoutConnectionString_registersNothing_andTheHostStillBuilds()
    {
        // The 3.x exporter throws at startup without a connection string, so local
        // dev and test hosts must not register it.
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "";

        Assert.False(ProgramSetup.AddApplicationInsights(builder));

        using var app = builder.Build();
        Assert.Null(app.Services.GetService<TracerProvider>());
    }

    [Fact]
    public void WithConnectionString_registersThePipeline()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://example.invalid/";
        builder.Services.AddHttpContextAccessor();

        Assert.True(ProgramSetup.AddApplicationInsights(builder));

        using var app = builder.Build();
        Assert.NotNull(app.Services.GetService<TracerProvider>());
    }
}
