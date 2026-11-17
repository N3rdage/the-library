using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Duplicates;

/// <summary>Records a candidate pair as not-a-duplicate so it moves to the
/// Dismissed section. The ids are stored lower-first, so either order names the
/// same pair. No-op when both ids are equal or the pair is already dismissed.</summary>
public sealed record DismissDuplicatePair(DuplicateEntityType Type, int IdA, int IdB, string? Note) : ICommand;

public sealed class DismissDuplicatePairHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<DismissDuplicatePair>
{
    public async Task HandleAsync(DismissDuplicatePair command, CancellationToken ct = default)
    {
        if (command.IdA == command.IdB) return;
        var (lower, higher) = command.IdA < command.IdB ? (command.IdA, command.IdB) : (command.IdB, command.IdA);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.IgnoredDuplicates
            .FirstOrDefaultAsync(d => d.EntityType == command.Type && d.LowerId == lower && d.HigherId == higher, ct);
        if (existing is not null) return;

        db.IgnoredDuplicates.Add(new IgnoredDuplicate
        {
            EntityType = command.Type,
            LowerId = lower,
            HigherId = higher,
            DismissedAt = DateTime.UtcNow,
            Note = command.Note
        });
        await db.SaveChangesAsync(ct);
    }
}
