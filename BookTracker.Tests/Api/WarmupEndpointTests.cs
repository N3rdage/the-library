using BookTracker.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace BookTracker.Tests.Api;

// Smoke test for /warmup. The endpoint is the slot-swap warmup probe
// target (WEBSITE_SWAP_WARMUP_PING_PATH=/warmup in app-config.bicep);
// if this 500s on deploy, the swap fails and traffic doesn't promote.
// The risk it guards against: someone refactors WarmupEndpoints.cs and
// breaks the DB-touch path, the endpoint still compiles + routes, but
// Azure's warmup probe returns 500 and the swap never completes.
//
// Hosts the full ProgramSetup on a random Kestrel port (same pattern
// as PlaywrightFixture) and hits /warmup via HttpClient.
[Trait("Category", TestCategories.Integration)]
public class WarmupEndpointTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            SqlServerContainer.ConnectionString);

        _app = ProgramSetup.Build(["--urls", "http://127.0.0.1:0"]);
        await _app.StartAsync();

        var server = _app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Server has no addresses feature");
        _http = new HttpClient { BaseAddress = new Uri(addresses.Addresses.First()) };
    }

    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
    }

    [Fact]
    public async Task GetWarmup_Returns200WithWarmBody()
    {
        var response = await _http.GetAsync("/warmup");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("warm", body);
    }
}
