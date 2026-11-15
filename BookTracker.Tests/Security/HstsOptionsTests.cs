using BookTracker.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BookTracker.Tests.Security;

// Guards the HSTS policy (SECURITY-AUDIT.md §11, audit rule BOOK-007).
// The wiring test builds the real host through ProgramSetup.Build in the
// Production environment and resolves the registered HstsOptions, so
// dropping the AddHsts(ConfigureHsts) registration fails it: the framework
// default would come back (30 days, same host only). Nothing is started
// and no database is touched; the environment is passed on the command
// line rather than via an environment variable so parallel test classes
// that also build the host are unaffected.
[Trait("Category", TestCategories.Unit)]
public class HstsOptionsTests
{
    [Fact]
    public async Task ProductionHost_RegistersOneYearIncludingSubdomainsWithoutPreload()
    {
        await using var app = ProgramSetup.Build(["--environment", "Production"]);

        var options = app.Services.GetRequiredService<IOptions<HstsOptions>>().Value;

        Assert.Equal(TimeSpan.FromDays(365), options.MaxAge);
        Assert.True(options.IncludeSubDomains);
        Assert.False(options.Preload);
    }

    [Fact]
    public void ConfigureHsts_MeetsTheSixMonthFloorOfRuleBook007()
    {
        var options = new HstsOptions();

        ProgramSetup.ConfigureHsts(options);

        Assert.True(options.MaxAge >= TimeSpan.FromDays(180));
        Assert.True(options.IncludeSubDomains);
    }
}
