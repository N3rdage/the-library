using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services.Covers;

// Periodic background service that walks Edition.CoverUrl and
// Book.DefaultCoverArtUrl looking for upstream URLs that haven't been
// mirrored to blob storage yet, and runs them through IBookCoverStorage.
//
// One service handles two cases — initial backfill on first deploy after
// this ships AND ongoing mirroring of new covers added by save flows. The
// trade-off is a brief lag (~PollInterval) between save and the row showing
// a blob URL; PR2 will move new-cover mirroring inline at the save site so
// the polling becomes backfill-only.
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Editions first — they're the canonical per-edition cover. Pull URL +
        // id, filter unmanaged in-app (EF can't translate the IsManagedUrl
        // check). Take just the first MaxRowsPerTick of each kind to bound
        // per-tick work.
        var editionCandidates = await db.Editions
            .Where(e => e.CoverUrl != null && e.CoverUrl != "")
            .OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.CoverUrl })
            .ToListAsync(ct);

        var unmirroredEditions = editionCandidates
            .Where(e => !coverStorage.IsManagedUrl(e.CoverUrl))
            .Take(MaxRowsPerTick)
            .ToList();

        foreach (var item in unmirroredEditions)
        {
            if (ct.IsCancellationRequested) return;

            var blobKey = $"editions/{item.Id}";
            var newUrl = await coverStorage.MirrorFromUrlAsync(item.CoverUrl!, blobKey, ct);
            if (newUrl != item.CoverUrl)
            {
                var fresh = await db.Editions.FirstOrDefaultAsync(x => x.Id == item.Id, ct);
                if (fresh is not null)
                {
                    fresh.CoverUrl = newUrl;
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("Mirrored cover for Edition {EditionId}.", item.Id);
                }
            }
        }

        // Books — DefaultCoverArtUrl. Same shape.
        var bookCandidates = await db.Books
            .Where(b => b.DefaultCoverArtUrl != null && b.DefaultCoverArtUrl != "")
            .OrderBy(b => b.Id)
            .Select(b => new { b.Id, Url = b.DefaultCoverArtUrl })
            .ToListAsync(ct);

        var unmirroredBooks = bookCandidates
            .Where(b => !coverStorage.IsManagedUrl(b.Url))
            .Take(MaxRowsPerTick)
            .ToList();

        foreach (var item in unmirroredBooks)
        {
            if (ct.IsCancellationRequested) return;

            var blobKey = $"books/{item.Id}";
            var newUrl = await coverStorage.MirrorFromUrlAsync(item.Url!, blobKey, ct);
            if (newUrl != item.Url)
            {
                var fresh = await db.Books.FirstOrDefaultAsync(x => x.Id == item.Id, ct);
                if (fresh is not null)
                {
                    fresh.DefaultCoverArtUrl = newUrl;
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("Mirrored default cover for Book {BookId}.", item.Id);
                }
            }
        }
    }
}
