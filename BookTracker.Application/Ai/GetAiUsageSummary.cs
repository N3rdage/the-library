using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Ai;

/// <summary>Price of one model in US dollars per million tokens, input and
/// output priced separately. Comes from configuration (AI:Prices in Web),
/// keyed by the model string the usage rows carry.</summary>
public sealed record AiModelPrice(decimal InputPerMillion, decimal OutputPerMillion);

/// <summary>Read-model for the usage line on /assistant: the AiUsage rows
/// grouped by provider and model, with calls and tokens summed, once for the
/// current UTC calendar month and once for all time. A line's cost is
/// computed from the given prices; a model with no price keeps its tokens
/// and gets a null cost. Price lookup is by exact model string,
/// case-insensitive.</summary>
public sealed record GetAiUsageSummary(IReadOnlyDictionary<string, AiModelPrice> Prices) : IQuery<AiUsageSummary>;

public sealed record AiUsageSummary(IReadOnlyList<AiUsageLine> ThisMonth, IReadOnlyList<AiUsageLine> AllTime);

public sealed record AiUsageLine(string Provider, string Model, int Calls, long InputTokens, long OutputTokens, decimal? CostUsd);

/// <summary>The cost arithmetic, kept pure so it is tested without SQL.</summary>
public static class AiUsageCost
{
    private const decimal TokensPerPrice = 1_000_000m;

    /// <summary>The cost of the given tokens at the given price, or null when
    /// the model has no price. The lookup honours the dictionary's own
    /// comparer first and falls back to a case-insensitive scan, so a price
    /// table built with the default comparer still matches "GPT-4o" to
    /// "gpt-4o".</summary>
    public static decimal? Compute(IReadOnlyDictionary<string, AiModelPrice> prices, string model, long inputTokens, long outputTokens)
    {
        var price = FindPrice(prices, model);
        if (price is null) return null;

        return inputTokens / TokensPerPrice * price.InputPerMillion
             + outputTokens / TokensPerPrice * price.OutputPerMillion;
    }

    private static AiModelPrice? FindPrice(IReadOnlyDictionary<string, AiModelPrice> prices, string model)
    {
        if (prices.TryGetValue(model, out var exact)) return exact;

        foreach (var (key, price) in prices)
        {
            if (string.Equals(key, model, StringComparison.OrdinalIgnoreCase)) return price;
        }
        return null;
    }
}

public sealed class GetAiUsageSummaryHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetAiUsageSummary, AiUsageSummary>
{
    public async Task<AiUsageSummary> HandleAsync(GetAiUsageSummary query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var thisMonth = await Summarise(db.AiUsages.AsNoTracking().Where(u => u.OccurredAt >= monthStart), query.Prices, ct);
        var allTime = await Summarise(db.AiUsages.AsNoTracking(), query.Prices, ct);
        return new AiUsageSummary(thisMonth, allTime);
    }

    private static async Task<IReadOnlyList<AiUsageLine>> Summarise(
        IQueryable<AiUsage> rows, IReadOnlyDictionary<string, AiModelPrice> prices, CancellationToken ct)
    {
        // Grouped and summed in SQL; only one row per (provider, model) crosses
        // the wire. The token sums are long because a month of calls can pass
        // int range even though no single call does.
        var groups = await rows
            .GroupBy(u => new { u.Provider, u.Model })
            .Select(g => new
            {
                g.Key.Provider,
                g.Key.Model,
                Calls = g.Count(),
                InputTokens = g.Sum(u => (long)u.InputTokens),
                OutputTokens = g.Sum(u => (long)u.OutputTokens),
            })
            .OrderBy(g => g.Provider).ThenBy(g => g.Model)
            .ToListAsync(ct);

        return groups
            .Select(g => new AiUsageLine(
                g.Provider, g.Model, g.Calls, g.InputTokens, g.OutputTokens,
                AiUsageCost.Compute(prices, g.Model, g.InputTokens, g.OutputTokens)))
            .ToList();
    }
}
