using System.Net;
using BookTracker.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace BookTracker.Tests.E2E;

/// <summary>
/// Hosts BookTracker.Web on a real Kestrel listener (random port) plus
/// a Playwright Chromium browser instance, both reused across tests in
/// the IClassFixture-wired suite.
///
/// Why not WebApplicationFactory&lt;Program&gt;: that helper hardcodes a
/// cast to TestServer in its Server getter, so swapping in real Kestrel
/// throws InvalidCastException. Calling ProgramSetup.Build directly
/// bypasses the helper entirely while still going through the same
/// configuration pipeline as Main.
///
/// DB connection points at the existing process-scoped MsSql
/// Testcontainer (the same singleton the Integration tests share).
/// Per-test isolation is best-effort for the POC — tests use unique
/// names (GUID suffix) to avoid collisions; a Respawn-based wipe layer
/// is a follow-up if pollution becomes a problem.
/// </summary>
public class PlaywrightFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Override the connection string via env var so ProgramSetup's
        // configuration pipeline picks it up (IConfiguration reads
        // ConnectionStrings__DefaultConnection from environment).
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            SqlServerContainer.ConnectionString);

        // --urls passes through to Kestrel via WebApplicationBuilder's
        // command-line config provider. Port 0 = OS-assigned random.
        _app = ProgramSetup.Build(["--urls", "http://127.0.0.1:0"]);

        // Skip ProgramSetup.RunMigrationsAsync — the Testcontainer DB
        // already has the schema applied via SqlServerContainer's
        // singleton init.

        await _app.StartAsync();

        var server = _app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Server has no addresses feature");
        BaseUrl = addresses.Addresses.First();

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
    }
}
