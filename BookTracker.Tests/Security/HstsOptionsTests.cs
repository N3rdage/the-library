using BookTracker.Web;
using Microsoft.AspNetCore.HttpsPolicy;

namespace BookTracker.Tests.Security;

[Trait("Category", TestCategories.Unit)]
public class HstsOptionsTests
{
    [Fact]
    public void ConfigureHsts_SetsOneYearIncludingSubdomainsWithoutPreload()
    {
        var options = new HstsOptions();

        ProgramSetup.ConfigureHsts(options);

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
    }
}
