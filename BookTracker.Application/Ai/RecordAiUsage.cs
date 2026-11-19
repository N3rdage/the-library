using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Ai;

/// <summary>Records one successful AI call: the provider and model that
/// answered, the operation that asked and the token counts the provider
/// reported. The providers dispatch it after every call; GetAiUsageSummary
/// reads the rows back. Stamps OccurredAt with the current UTC time.</summary>
public sealed record RecordAiUsage(string Provider, string Model, string Operation, int InputTokens, int OutputTokens) : ICommand;

public sealed class RecordAiUsageHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RecordAiUsage>
{
    public async Task HandleAsync(RecordAiUsage command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        db.AiUsages.Add(new AiUsage
        {
            OccurredAt = DateTime.UtcNow,
            Provider = command.Provider,
            Model = command.Model,
            Operation = command.Operation,
            InputTokens = command.InputTokens,
            OutputTokens = command.OutputTokens,
        });
        await db.SaveChangesAsync(ct);
    }
}
