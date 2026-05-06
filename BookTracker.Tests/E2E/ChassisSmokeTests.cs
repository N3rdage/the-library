using Microsoft.Playwright;

namespace BookTracker.Tests.E2E;

/// <summary>
/// First Playwright test — proves the chassis end-to-end: real Kestrel
/// + real Chromium + real DB (Testcontainer). Two tests:
///
///  - <c>HomePage_Renders</c> covers the simplest possible path: server
///    renders, browser receives, page contains the expected content.
///    Catches "fixture broken / port not bound / DB not reachable" at
///    PR time.
///
///  - <c>SeriesListPage_RendersHeading</c> exercises a one-hop
///    in-app navigation so the assertion isn't only against the
///    landing page.
///
/// Deliberately scoped *not* to the form-submit + redirect flow we
/// spent four iterations fixing (#168) — that has a real
/// Blazor-static-SSR + interactive-mode interaction that warrants its
/// own investigation. POC scope is "prove the loop." A follow-up PR
/// adds a CreateSeries regression test once the form-submit harness
/// is sorted.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public class ChassisSmokeTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IBrowserContext? _context;
    private IPage? _page;

    public ChassisSmokeTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = _fixture.BaseUrl,
            IgnoreHTTPSErrors = true,
        });
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context is not null) await _context.DisposeAsync();
    }

    [Fact]
    public async Task HomePage_Renders()
    {
        await _page!.GotoAsync("/");

        // Home renders with the BookTracker brand in the navbar — proves
        // server bound to Kestrel, browser fetched the page, HTML
        // returned with expected content. If this fails, every other
        // E2E test is doomed; isolate the chassis from feature concerns.
        var brand = _page.Locator(".navbar-brand");
        await Assertions.Expect(brand).ToContainTextAsync("BookTracker");
    }

    [Fact]
    public async Task SeriesListPage_RendersHeading()
    {
        await _page!.GotoAsync("/series");

        // One-hop navigation off the home page, asserting against the
        // route component's specific content. Catches routing and
        // page-level rendering regressions that the home page wouldn't.
        var heading = _page.Locator("h1");
        await Assertions.Expect(heading).ToContainTextAsync("Series");
    }
}
