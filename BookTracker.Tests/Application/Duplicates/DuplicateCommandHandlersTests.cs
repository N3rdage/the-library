using BookTracker.Application.Duplicates;
using BookTracker.Data.Models;

namespace BookTracker.Tests.Application.Duplicates;

[Trait("Category", TestCategories.Integration)]
public class DuplicateCommandHandlersTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task DismissAsync(DuplicateEntityType type, int idA, int idB, string? note) =>
        new DismissDuplicatePairHandler(_factory).HandleAsync(new DismissDuplicatePair(type, idA, idB, note));

    private Task RestoreAsync(int ignoredDuplicateId) =>
        new RestoreDismissedDuplicateHandler(_factory).HandleAsync(new RestoreDismissedDuplicate(ignoredDuplicateId));

    private Task SweepAsync() =>
        new SweepOrphanedDismissalsHandler(_factory).HandleAsync(new SweepOrphanedDismissals());

    private Task<DuplicateReport> DetectAsync() =>
        new GetDuplicateReportHandler(_factory).HandleAsync(new GetDuplicateReport());

    // ─── Dismiss / Unignore ───────────────────────────────────────────

    [Fact]
    public async Task Dismiss_moves_pair_to_dismissed_list_and_carries_ignored_id()
    {
        var (a, b) = await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");

        await DismissAsync(DuplicateEntityType.Author, a, b, "intentional separate entries");

        var report = await DetectAsync();
        Assert.Single(report.Authors);
        var pair = report.Authors[0];
        Assert.NotNull(pair.Dismissed);
        Assert.Equal("intentional separate entries", pair.Dismissed!.Note);
    }

    [Fact]
    public async Task Dismiss_is_idempotent()
    {
        var (a, b) = await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");

        await DismissAsync(DuplicateEntityType.Author, a, b, null);
        await DismissAsync(DuplicateEntityType.Author, a, b, "second call");

        using var db = _factory.CreateDbContext();
        Assert.Single(db.IgnoredDuplicates);
    }

    [Fact]
    public async Task Dismiss_normalises_id_order()
    {
        var (a, b) = await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");

        // Pass IDs in reverse — should still dedupe with the lower/higher row.
        await DismissAsync(DuplicateEntityType.Author, b, a, null);
        await DismissAsync(DuplicateEntityType.Author, a, b, null);

        using var db = _factory.CreateDbContext();
        Assert.Single(db.IgnoredDuplicates);
    }

    [Fact]
    public async Task Unignore_removes_row_and_pair_returns_to_active()
    {
        var (a, b) = await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");

        await DismissAsync(DuplicateEntityType.Author, a, b, null);
        var afterDismiss = await DetectAsync();
        var ignoredId = afterDismiss.Authors[0].Dismissed!.IgnoredDuplicateId;

        await RestoreAsync(ignoredId);

        var afterUnignore = await DetectAsync();
        Assert.Null(afterUnignore.Authors[0].Dismissed);
    }

    // ─── Orphan cleanup ───────────────────────────────────────────────

    [Fact]
    public async Task Sweep_keeps_valid_dismissals_and_removes_only_orphans()
    {
        var (a, b) = await SeedAuthorsAsync("J.R.R. Tolkien", "JRR Tolkien");
        var (c, d) = await SeedAuthorsAsync("Doug Preston", "Douglas Preston");
        await DismissAsync(DuplicateEntityType.Author, a, b, "keep me");
        await DismissAsync(DuplicateEntityType.Author, c, d, null);

        using (var db = _factory.CreateDbContext())
        {
            // Orphan by deletion: one side of the second pair no longer exists.
            db.Authors.Remove(db.Authors.Single(x => x.Id == d));
            // Orphan by id that never existed.
            db.IgnoredDuplicates.Add(new IgnoredDuplicate
            {
                EntityType = DuplicateEntityType.Author,
                LowerId = Math.Max(a, b) + 1000,
                HigherId = Math.Max(a, b) + 1001,
                DismissedAt = DateTime.UtcNow
            });
            // Orphan by entity type: the ids are real Authors but no such Works exist.
            db.IgnoredDuplicates.Add(new IgnoredDuplicate
            {
                EntityType = DuplicateEntityType.Work,
                LowerId = Math.Min(a, b),
                HigherId = Math.Max(a, b),
                DismissedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await SweepAsync();

        using var verify = _factory.CreateDbContext();
        var survivor = Assert.Single(verify.IgnoredDuplicates);
        Assert.Equal(DuplicateEntityType.Author, survivor.EntityType);
        Assert.Equal(Math.Min(a, b), survivor.LowerId);
        Assert.Equal(Math.Max(a, b), survivor.HigherId);
        Assert.Equal("keep me", survivor.Note);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private async Task<(int, int)> SeedAuthorsAsync(params string[] names)
    {
        using var db = _factory.CreateDbContext();
        var authors = names.Select(n => Author.Create(n)).ToList();
        db.Authors.AddRange(authors);
        await db.SaveChangesAsync();
        return (authors[0].Id, authors.Count > 1 ? authors[1].Id : 0);
    }
}
