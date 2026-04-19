using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

// One-shot startup task that re-classifies existing Editions using the
// (now-richer) BookFormat enum populated from upstream metadata. The first
// successful run records a MaintenanceLog row and subsequent startups
// short-circuit to a no-op. Runs in the background so app startup isn't
// blocked.
public class EditionFormatBackfillService(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IBookLookupService lookup,
    ILogger<EditionFormatBackfillService> logger) : BackgroundService
{
    private const string MarkerName = "BackfillEditionFormats-v1";

    // Polite delay between Open Library calls. Settable so unit tests can
    // skip the wait; production never overrides it.
    public TimeSpan ApiThrottle { get; init; } = TimeSpan.FromMilliseconds(200);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunBackfillAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // App shutting down before the backfill finished — leave the
            // marker absent so it retries on next start.
        }
        catch (Exception ex)
        {
            // Never fail the host because of a backfill problem; log and move on.
            logger.LogError(ex, "Edition format backfill failed unexpectedly");
        }
    }

    // Exposed (rather than private) so tests can drive the backfill
    // synchronously without the BackgroundService scaffolding.
    public async Task RunBackfillAsync(CancellationToken ct)
    {
        await using (var probe = await dbFactory.CreateDbContextAsync(ct))
        {
            if (await probe.MaintenanceLogs.AnyAsync(m => m.Name == MarkerName, ct))
            {
                logger.LogDebug("Edition format backfill already completed; skipping");
                return;
            }
        }

        logger.LogInformation("Starting edition format backfill");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var editions = await db.Editions.ToListAsync(ct);

        var updated = 0;
        var failures = 0;

        foreach (var edition in editions)
        {
            ct.ThrowIfCancellationRequested();

            // Skip pre-1974 (no-ISBN) editions — there's nothing to look up.
            if (string.IsNullOrWhiteSpace(edition.Isbn)) continue;

            try
            {
                var result = await lookup.LookupByIsbnAsync(edition.Isbn, ct);
                if (result?.Format is BookFormat resolved && resolved != edition.Format)
                {
                    edition.Format = resolved;
                    updated++;
                }
            }
            catch (Exception ex)
            {
                failures++;
                logger.LogWarning(ex, "Backfill lookup failed for ISBN {Isbn}", edition.Isbn);
            }

            try
            {
                await Task.Delay(ApiThrottle, ct);
            }
            catch (OperationCanceledException) { throw; }
        }

        await db.SaveChangesAsync(ct);

        db.MaintenanceLogs.Add(new MaintenanceLog
        {
            Name = MarkerName,
            CompletedAt = DateTime.UtcNow,
            Notes = $"Updated {updated} of {editions.Count} editions; {failures} lookup failures."
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Edition format backfill complete: updated {Updated}/{Total}, {Failures} failures",
            updated, editions.Count, failures);
    }
}
