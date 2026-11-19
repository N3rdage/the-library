using BookTracker.Application.Ai;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests;

// Integration tests for the AI usage bookkeeping: RecordAiUsage writes one row
// per call, GetAiUsageSummary groups the rows by provider and model for the
// current UTC month and for all time. The cost arithmetic itself is covered by
// AiUsageCostTests.
[Trait("Category", TestCategories.Integration)]
public class AiUsageHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private static readonly Dictionary<string, AiModelPrice> Prices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-sonnet-4-6"] = new(3.00m, 15.00m),
    };

    private async Task SeedAsync(params AiUsage[] rows)
    {
        await using var db = _factory.CreateDbContext();
        db.AiUsages.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static AiUsage Row(DateTime occurredAt, string provider, string model, int input, int output, string operation = "SuggestGenres") =>
        new() { OccurredAt = occurredAt, Provider = provider, Model = model, Operation = operation, InputTokens = input, OutputTokens = output };

    [Fact]
    public async Task RecordAiUsage_writesOneRow_stampedNowInUtc()
    {
        var before = DateTime.UtcNow;

        await new RecordAiUsageHandler(_factory).HandleAsync(
            new RecordAiUsage("Anthropic", "claude-sonnet-4-6", "AssessBook", 1200, 340));

        await using var db = _factory.CreateDbContext();
        var row = await db.AiUsages.SingleAsync();
        Assert.Equal("Anthropic", row.Provider);
        Assert.Equal("claude-sonnet-4-6", row.Model);
        Assert.Equal("AssessBook", row.Operation);
        Assert.Equal(1200, row.InputTokens);
        Assert.Equal(340, row.OutputTokens);
        Assert.Equal(DateTimeKind.Utc, row.OccurredAt.Kind);
        // datetime2 keeps 100ns ticks but SQL Server rounds, so allow a second each side.
        Assert.InRange(row.OccurredAt, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task Summary_splitsThisMonthFromAllTime_atTheUtcMonthStart()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedAsync(
            Row(monthStart, "Anthropic", "claude-sonnet-4-6", 100, 10),             // first instant of the month counts
            Row(monthStart.AddSeconds(-1), "Anthropic", "claude-sonnet-4-6", 1000, 100), // last month
            Row(monthStart.AddMonths(-3), "Anthropic", "claude-sonnet-4-6", 5000, 500));

        var summary = await new GetAiUsageSummaryHandler(_factory).HandleAsync(new GetAiUsageSummary(Prices));

        var month = Assert.Single(summary.ThisMonth);
        Assert.Equal(1, month.Calls);
        Assert.Equal(100, month.InputTokens);
        Assert.Equal(10, month.OutputTokens);

        var all = Assert.Single(summary.AllTime);
        Assert.Equal(3, all.Calls);
        Assert.Equal(6100, all.InputTokens);
        Assert.Equal(610, all.OutputTokens);
    }

    [Fact]
    public async Task Summary_groupsByProviderAndModel_orderedByBoth_andPricesOnlyKnownModels()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(
            Row(now, "Anthropic", "claude-sonnet-4-6", 1_000_000, 0, "SuggestGenres"),
            Row(now, "Anthropic", "claude-sonnet-4-6", 0, 1_000_000, "AssessBook"),   // same model, different operation: one line
            Row(now, "Anthropic", "claude-opus-4-6", 10, 20),                         // no price
            Row(now, "AzureOpenAI", "gpt-4o", 30, 40));                               // no price

        var summary = await new GetAiUsageSummaryHandler(_factory).HandleAsync(new GetAiUsageSummary(Prices));

        Assert.Equal(
            new[] { ("Anthropic", "claude-opus-4-6"), ("Anthropic", "claude-sonnet-4-6"), ("AzureOpenAI", "gpt-4o") },
            summary.ThisMonth.Select(l => (l.Provider, l.Model)));

        var sonnet = summary.ThisMonth[1];
        Assert.Equal(2, sonnet.Calls);
        Assert.Equal(1_000_000, sonnet.InputTokens);
        Assert.Equal(1_000_000, sonnet.OutputTokens);
        Assert.Equal(18.00m, sonnet.CostUsd);                 // $3 in + $15 out per million

        Assert.Null(summary.ThisMonth[0].CostUsd);
        Assert.Equal(10, summary.ThisMonth[0].InputTokens);   // tokens still shown without a price
        Assert.Null(summary.ThisMonth[2].CostUsd);
    }

    [Fact]
    public async Task Summary_withNoRows_isTwoEmptyLists()
    {
        var summary = await new GetAiUsageSummaryHandler(_factory).HandleAsync(new GetAiUsageSummary(Prices));

        Assert.Empty(summary.ThisMonth);
        Assert.Empty(summary.AllTime);
    }
}
