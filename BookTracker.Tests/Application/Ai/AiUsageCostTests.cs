using BookTracker.Application.Ai;

namespace BookTracker.Tests;

// The cost arithmetic behind the usage line on /assistant: tokens priced per
// million against the AI:Prices table, keyed by model, case-insensitive.
[Trait("Category", TestCategories.Unit)]
public class AiUsageCostTests
{
    private static readonly Dictionary<string, AiModelPrice> Prices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-sonnet-4-6"] = new(3.00m, 15.00m),
        ["gpt-4o"] = new(2.50m, 10.00m),
    };

    [Fact]
    public void PricedModel_isInputPlusOutput_eachPerMillion()
    {
        // 84,210 in at $3/M = 0.25263; 6,120 out at $15/M = 0.0918
        Assert.Equal(0.34443m, AiUsageCost.Compute(Prices, "claude-sonnet-4-6", 84_210, 6_120));
    }

    [Fact]
    public void UnpricedModel_isNull()
    {
        Assert.Null(AiUsageCost.Compute(Prices, "claude-opus-4-6", 1_000_000, 1_000_000));
    }

    [Fact]
    public void ModelKey_isCaseInsensitive_evenWhenTheTableUsesTheDefaultComparer()
    {
        var caseSensitiveTable = new Dictionary<string, AiModelPrice> { ["gpt-4o"] = new(2.50m, 10.00m) };

        Assert.Equal(12.50m, AiUsageCost.Compute(Prices, "GPT-4O", 1_000_000, 1_000_000));
        Assert.Equal(12.50m, AiUsageCost.Compute(caseSensitiveTable, "GPT-4O", 1_000_000, 1_000_000));
    }

    [Fact]
    public void ZeroTokens_costZero_notNull()
    {
        Assert.Equal(0m, AiUsageCost.Compute(Prices, "gpt-4o", 0, 0));
    }
}
