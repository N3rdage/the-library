using BookTracker.Mobile.Cache;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace BookTracker.Mobile.Services;

/// <summary>
/// Owns the app's auth + catalog-sync state and operations, lifted out of
/// MainPage so the status sheet, the ambient SyncChip, and auto-sync-on-launch
/// all share one source of truth. Singleton — one sync state per app process.
/// Raises <see cref="StateChanged"/> whenever sign-in, connectivity, or cache
/// meta changes so headers/sheets can re-render.
/// </summary>
public interface ISyncService
{
    /// <summary>Last-known cache metadata (book/author counts, last-synced,
    /// watermark). Null until <see cref="RefreshMetaAsync"/> runs / no cache.</summary>
    CacheMeta? Meta { get; }
    bool IsSignedIn { get; }
    bool IsOnline { get; }
    event EventHandler? StateChanged;

    /// <summary>Opens the SQLite cache. Idempotent — safe to call repeatedly.</summary>
    Task InitAsync();
    Task<bool> RefreshSignInAsync();
    Task SignInAsync();
    Task SignOutAsync();
    /// <summary>Runs the catalog delta-sync (full / delta / version-changed
    /// decision tree) and refreshes <see cref="Meta"/>. Returns a status line.</summary>
    Task<string> SyncCatalogAsync();
    Task RefreshMetaAsync();
}

public sealed class SyncService : ISyncService
{
    private readonly IAuthService _auth;
    private readonly IApiClient _api;
    private readonly ICatalogCache _cache;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private bool _initialised;

    public SyncService(IAuthService auth, IApiClient api, ICatalogCache cache)
    {
        _auth = auth;
        _api = api;
        _cache = cache;
        IsOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    public CacheMeta? Meta { get; private set; }
    public bool IsSignedIn { get; private set; }
    public bool IsOnline { get; private set; }
    public event EventHandler? StateChanged;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        IsOnline = e.NetworkAccess == NetworkAccess.Internet;
        Raise();
    }

    public async Task InitAsync()
    {
        if (_initialised) return;
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "catalog.db");
        await _cache.InitAsync(dbPath);
        _initialised = true;
    }

    public async Task<bool> RefreshSignInAsync()
    {
        IsSignedIn = await _auth.IsSignedInAsync();
        Raise();
        return IsSignedIn;
    }

    public async Task SignInAsync()
    {
        await _auth.SignInAsync();
        IsSignedIn = true;
        Raise();
    }

    public async Task SignOutAsync()
    {
        await _auth.SignOutAsync();
        IsSignedIn = false;
        // Cache data is intentionally NOT wiped — it outlives sign-out so the
        // offline surfaces keep working.
        Raise();
    }

    public async Task RefreshMetaAsync()
    {
        try { Meta = await _cache.GetMetaAsync(); }
        catch { Meta = null; } // corrupt / un-init'd cache — render as "no cache".
        Raise();
    }

    public async Task<string> SyncCatalogAsync()
    {
        // One sync at a time — launch auto-sync (AppShell) and a manual Refresh
        // (status sheet) must not both write the cache concurrently. A second
        // caller backs off rather than interleaving PopulateAsync/ApplyDeltaAsync.
        if (!await _syncGate.WaitAsync(0))
            return "Sync already in progress…";
        try
        {
            // Delta-sync decision tree (lifted verbatim from the old
            // MainPage.OnLoadCatalogClicked):
            //   1. No watermark → first load → full PopulateAsync.
            //   2. Have watermark → GET ?since=<token>.
            //      a. Version matches → merge deltas via ApplyDeltaAsync.
            //      b. Version differs → server deploy invalidated cache shape;
            //         re-fetch WITHOUT the token for a true full snapshot before
            //         wiping (else a delta-window populate drops everything older).
            var storedToken = Meta?.LatestUpdatedAt;
            var storedVersion = Meta?.Version;
            var snapshot = await _api.GetCatalogSnapshotAsync(since: storedToken);

            string statusSuffix;
            if (storedToken is null)
            {
                await _cache.PopulateAsync(snapshot);
                statusSuffix = "full";
            }
            else if (!string.Equals(snapshot.Version, storedVersion, StringComparison.Ordinal))
            {
                snapshot = await _api.GetCatalogSnapshotAsync(since: null);
                await _cache.PopulateAsync(snapshot);
                statusSuffix = "full (version changed)";
            }
            else
            {
                await _cache.ApplyDeltaAsync(snapshot);
                var bookChanges = snapshot.Books?.Count ?? 0;
                var deletes = snapshot.DeletedIds?.Count ?? 0;
                statusSuffix = bookChanges == 0 && deletes == 0
                    ? "delta · no changes"
                    : $"delta · {bookChanges} updated, {deletes} removed";
            }

            await RefreshMetaAsync();
            return snapshot.Series is null
                ? $"Synced ✓ ({statusSuffix}, server lacks /series field — redeploy Web)"
                : $"Synced ✓ ({statusSuffix})";
        }
        finally
        {
            _syncGate.Release();
        }
    }

    // OnConnectivityChanged fires on a non-UI thread, so marshal here once —
    // every StateChanged subscriber can then touch UI without its own guard.
    private void Raise()
    {
        if (MainThread.IsMainThread)
            StateChanged?.Invoke(this, EventArgs.Empty);
        else
            MainThread.BeginInvokeOnMainThread(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Relative-time label for a last-synced timestamp:
    /// "never" / "just now" / "5m ago" / "2h ago" / "2026-05-12 14:23".
    /// Shared by the status sheet + the SyncChip.</summary>
    public static string FormatAge(DateTime? syncedUtc)
    {
        if (syncedUtc is not { } utc) return "never";
        var ago = DateTime.UtcNow - utc;
        if (ago.TotalMinutes < 1) return "just now";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24) return $"{(int)ago.TotalHours}h ago";
        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
