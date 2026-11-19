using BookTracker.Application.Ai;
using BookTracker.Web.Services;
using Microsoft.Extensions.Configuration;

namespace BookTracker.Tests;

// AI:Prices binds from configuration into AIOptions.Prices: one positional
// AiModelPrice record per model key, and the dictionary keeps its
// case-insensitive key comparer through binding.
[Trait("Category", TestCategories.Unit)]
public class AIOptionsPricesBindingTests
{
    private static AIOptions Bind(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var options = new AIOptions();
        configuration.GetSection(AIOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void Prices_bindPerModel_asPositionalRecords()
    {
        var options = Bind(
            ("AI:Prices:claude-sonnet-4-6:InputPerMillion", "3.00"),
            ("AI:Prices:claude-sonnet-4-6:OutputPerMillion", "15.00"),
            ("AI:Prices:gpt-4o:InputPerMillion", "2.50"),
            ("AI:Prices:gpt-4o:OutputPerMillion", "10.00"));

        Assert.Equal(2, options.Prices.Count);
        Assert.Equal(new AiModelPrice(3.00m, 15.00m), options.Prices["claude-sonnet-4-6"]);
        Assert.Equal(new AiModelPrice(2.50m, 10.00m), options.Prices["gpt-4o"]);
    }

    [Fact]
    public void Prices_keysStayCaseInsensitive_afterBinding()
    {
        var options = Bind(
            ("AI:Prices:gpt-4o:InputPerMillion", "2.50"),
            ("AI:Prices:gpt-4o:OutputPerMillion", "10.00"));

        Assert.True(options.Prices.ContainsKey("GPT-4O"));
    }

    [Fact]
    public void NoPricesSection_leavesTheTableEmpty()
    {
        Assert.Empty(Bind(("AI:DefaultProvider", "Anthropic")).Prices);
    }
}
