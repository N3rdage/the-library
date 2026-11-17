using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services.Covers;

// Periodic background service that walks Edition.CoverUrl and
// Book.DefaultCoverArtUrl looking for upstream URLs that haven't been
// mirrored to blob storage yet, and runs them through IBookCoverStorage.
// The same poll covers the backfill of URLs already in the database and the
// mirroring of newly added covers, at the cost of up to one PollInterval of
// lag between a save and the row showing a blob URL.
//
// Each unsuccessful attempt is recorded in a CoverMirrorState row
// (CoverMirrorQueue). A transient failure (5xx, 408, 429, timeout, network or
// blob-upload error) is retried on a doubling backoff from one minute up to
// 24 hours. A permanent refusal (refused URL, other 4xx, disallowed content
// type, over the size cap, undecodable image) is not retried while the row
// keeps that URL; a changed URL makes the row due again at once. A successful
// mirror deletes the state row, and each tick sweeps state rows whose Edition
// or Book is gone.
//
// Idempotent and safe to run on every instance — IsManagedUrl gates work
// per row so a row mirrored by one tick won't re-process on the next.
public class CoverMirrorBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<CoverMirrorBackgroundService> logger) : BackgroundService
{
    /// <summary>How often to scan for unmirrored URLs. 30s keeps the lag short
    /// without burning CPU on a single-instance App Service.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    /// <summary>Per-tick row cap — protects against a backfill of thousands of
    /// covers blocking shutdown on the first tick. Subsequent ticks finish
    /// the rest.</summary>
    private const int MaxRowsPerTick = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Quick gate: if storage is disabled, log once and idle. Resolving via
        // scope here matches the per-tick pattern below — IBookCoverStorage is
        // a typed HttpClient and shouldn't be captured by a singleton.
        using (var probeScope = scopeFactory.CreateScope())
        {
            var probeStorage = probeScope.ServiceProvider.GetRequiredService<IBookCoverStorage>();
            if (!probeStorage.IsEnabled)
            {
                logger.LogInformation("Cover storage disabled — mirror service is idle.");
                return;
            }
        }

        // Small startup delay so the first tick doesn't race app initialisation.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cover mirror tick failed; will retry next interval.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ProcessTickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var coverStorage = scope.ServiceProvider.GetRequiredService<IBookCoverStorage>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BookTrackerDbContext>>();
        await ProcessTickAsync(coverStorage, dbFactory, logger, ct);
    }

    internal static async Task ProcessTickAsync(
        IBookCoverStorage coverStorage,
        IDbContextFactory<BookTrackerDbContext> dbFactory,
        ILogger logger,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;

        // Editions first — they're the canonical per-edition cover. The queue
        // returns rows not held back by their mirror state; the unmanaged
        // filter runs in-app (EF can't translate IsManagedUrl), and only the
        // first MaxRowsPerTick of each kind are processed to bound per-tick work.
        var dueEditions = await CoverMirrorQueue.DueEditionsAsync(db, nowUtc, ct);
        var unmirroredEditions = dueEditions
            .Where(e => !coverStorage.IsManagedUrl(e.Url))
            .Take(MaxRowsPerTick)
            .ToList();

        foreach (var item in unmirroredEditions)
        {
            if (ct.IsCancellationRequested) return;

            var result = await coverStorage.MirrorFromUrlAsync(item.Url, $"editions/{item.Id}", ct);
            if (result.Status == CoverMirrorStatus.Mirrored)
            {
                var fresh = await db.Editions.FirstOrDefaultAsync(x => x.Id == item.Id, ct);
                if (fresh is not null && fresh.CoverUrl != item.Url)
                {
                    // The state row is left alone: it can only hold the row back while
                    // its Url equals the current one, so the changed URL stays due.
                    logger.LogInformation(
                        "Cover for Edition {EditionId} changed during the mirror; the write is skipped.", item.Id);
                    continue;
                }
                if (fresh is not null)
                {
                    fresh.CoverUrl = result.Url;
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("Mirrored cover for Edition {EditionId}.", item.Id);
                }
                await CoverMirrorQueue.ClearAsync(db, CoverMirrorTarget.Edition, item.Id, ct);
            }
            else
            {
                await RecordUnsuccessfulAsync(db, CoverMirrorTarget.Edition, item, result.Status, nowUtc, ct);
            }
        }

        // Books — DefaultCoverArtUrl. Same shape.
        var dueBooks = await CoverMirrorQueue.DueBooksAsync(db, nowUtc, ct);
        var unmirroredBooks = dueBooks
            .Where(b => !coverStorage.IsManagedUrl(b.Url))
            .Take(MaxRowsPerTick)
            .ToList();

        foreach (var item in unmirroredBooks)
        {
            if (ct.IsCancellationRequested) return;

            var result = await coverStorage.MirrorFromUrlAsync(item.Url, $"books/{item.Id}", ct);
            if (result.Status == CoverMirrorStatus.Mirrored)
            {
                var fresh = await db.Books.FirstOrDefaultAsync(x => x.Id == item.Id, ct);
                if (fresh is not null && fresh.DefaultCoverArtUrl != item.Url)
                {
                    // As for editions: the untouched state row cannot hold back a changed URL.
                    logger.LogInformation(
                        "Default cover for Book {BookId} changed during the mirror; the write is skipped.", item.Id);
                    continue;
                }
                if (fresh is not null)
                {
                    fresh.DefaultCoverArtUrl = result.Url;
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("Mirrored default cover for Book {BookId}.", item.Id);
                }
                await CoverMirrorQueue.ClearAsync(db, CoverMirrorTarget.Book, item.Id, ct);
            }
            else
            {
                await RecordUnsuccessfulAsync(db, CoverMirrorTarget.Book, item, result.Status, nowUtc, ct);
            }
        }

        await CoverMirrorQueue.RemoveOrphansAsync(db, ct);
    }

    // Maps a non-mirrored status to the outcome stored on the state row.
    // AlreadyManaged needs nothing recorded.
    private static Task RecordUnsuccessfulAsync(
        BookTrackerDbContext db,
        CoverMirrorTarget target,
        CoverMirrorCandidate item,
        CoverMirrorStatus status,
        DateTime nowUtc,
        CancellationToken ct) => status switch
    {
        CoverMirrorStatus.PermanentRefusal => CoverMirrorQueue.RecordFailureAsync(
            db, target, item.Id, item.Url, CoverMirrorOutcome.PermanentRefusal, nowUtc, ct),
        CoverMirrorStatus.TransientFailure => CoverMirrorQueue.RecordFailureAsync(
            db, target, item.Id, item.Url, CoverMirrorOutcome.TransientFailure, nowUtc, ct),
        _ => Task.CompletedTask,
    };
}
