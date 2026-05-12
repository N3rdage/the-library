using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Pages;
using BookTracker.Mobile.Services;

namespace BookTracker.Mobile;

public partial class MainPage : ContentPage
{
    private readonly IAuthService _auth;
    private readonly IApiClient _api;
    private readonly ICatalogCache _cache;

    private bool _signedIn;
    private bool _busy;
    // Last-known cache metadata. Refreshed on app appearing, after
    // sign-in, and after Load catalog. RefreshUi() reads from this
    // (sync) rather than touching the cache directly so the render
    // path stays cheap.
    private CacheMeta? _meta;

    public MainPage(IAuthService auth, IApiClient api, ICatalogCache cache)
    {
        InitializeComponent();
        _auth = auth;
        _api = api;
        _cache = cache;
        RefreshUi();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Open the SQLite cache for the lifetime of the app process.
        // InitAsync is idempotent so calling on every appearing is
        // fine — second-and-later calls return immediately.
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "catalog.db");
        try
        {
            await _cache.InitAsync(dbPath);
        }
        catch (Exception ex)
        {
            // Cache failure shouldn't block the sign-in UX; surface
            // it as status text but let the user still try to
            // sign in + load (the no-cache path).
            StatusLabel.Text = $"Cache init failed: {ex.Message}";
        }

        // Refresh the meta panel regardless of sign-in state — cached
        // catalog data outlives sign-out, so the panel should show what
        // we actually have locally.
        await RefreshMetaAsync();

        if (await _auth.IsSignedInAsync())
        {
            _signedIn = true;
            StatusLabel.Text = _meta is null
                ? "Signed in. Tap Load catalog snapshot to begin."
                : "Signed in.";
        }

        RefreshUi();
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync("Signing in…", async () =>
        {
            await _auth.SignInAsync();
            _signedIn = true;
            await RefreshMetaAsync();
            return _meta is null
                ? "Signed in. Tap Load catalog snapshot to begin."
                : "Signed in.";
        });
    }

    private async void OnLoadCatalogClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync("Loading catalog…", async () =>
        {
            var snapshot = await _api.GetCatalogSnapshotAsync();
            // Atomic populate — a partial fill can't leave the DB
            // half-shrunken. PR 4's defensive null coalesces inside
            // PopulateAsync mean a server that hasn't shipped /series
            // yet still hydrates Books + Authors without throwing.
            await _cache.PopulateAsync(snapshot);
            await RefreshMetaAsync();

            // Surface the redeploy hint only when the server is
            // genuinely lagging on the /series field — the original
            // PR 4 NRE diagnostic kept around as a passive warning.
            return snapshot.Series is null
                ? "Catalog loaded ✓ (server lacks /series field — redeploy Web)"
                : "Catalog loaded ✓";
        });
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync("Signing out…", async () =>
        {
            await _auth.SignOutAsync();
            _signedIn = false;
            // Note: cache data is intentionally not wiped on sign-out.
            // The stats panel keeps showing what's locally stashed.
            return "Signed out.";
        });
    }

    // Runs an async action with the spinner up + buttons disabled,
    // then renders the returned status string. Catches + surfaces
    // any exception with enough breadcrumbs to diagnose without
    // dumping the full stack trace to the UI.
    private async Task RunBusyAsync(string busyStatus, Func<Task<string>> action)
    {
        _busy = true;
        StatusLabel.Text = busyStatus;
        RefreshUi();
        try
        {
            var result = await action();
            StatusLabel.Text = result;
        }
        catch (Exception ex)
        {
            // For NullReferenceException the canonical message is
            // useless ("Object reference not set..."). Capture the
            // exception type + the first stack frame so we can see
            // where the throw originated. Limit to ~2-3 lines so
            // the label stays readable on a phone.
            var topFrame = ex.StackTrace?
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim() ?? "(no stack)";
            StatusLabel.Text =
                $"Failed: {ex.GetType().Name}\n{ex.Message}\n→ {topFrame}";
        }
        finally
        {
            _busy = false;
            RefreshUi();
        }
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        // Resolve the page from DI so it gets ICatalogCache injected.
        // Transient registration in MauiProgram — each navigation
        // gets a fresh CameraBarcodeReaderView, no held-camera between
        // visits.
        var services = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("ServiceProvider not available.");
        var page = services.GetService(typeof(ScanPage)) as ScanPage
            ?? throw new InvalidOperationException("ScanPage not registered.");
        await Navigation.PushAsync(page);
    }

    private async void OnFindByAuthorClicked(object? sender, EventArgs e)
    {
        // Same DI pattern as OnScanClicked. AuthorSearchPage is
        // transient so each visit gets fresh debounce state.
        var services = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("ServiceProvider not available.");
        var page = services.GetService(typeof(AuthorSearchPage)) as AuthorSearchPage
            ?? throw new InvalidOperationException("AuthorSearchPage not registered.");
        await Navigation.PushAsync(page);
    }

    private async Task RefreshMetaAsync()
    {
        try
        {
            _meta = await _cache.GetMetaAsync();
        }
        catch
        {
            // GetMetaAsync only throws if the cache file is corrupt
            // or InitAsync wasn't called — both already surfaced
            // upstream. Treat as "no cache" here so RefreshUi keeps
            // rendering something sensible.
            _meta = null;
        }
    }

    private void RefreshUi()
    {
        Busy.IsRunning = _busy;
        var hasCache = _meta is not null;

        // Signed-out flow: only Sign in is visible / enabled.
        SignInButton.IsVisible = !_signedIn;
        SignInButton.IsEnabled = !_busy && !_signedIn;

        // Signed-in flow: Load catalog + Scan + Find by author visible.
        // Load stays visible after first load so the user can tap to
        // refresh. Scan + Find-by-author both need cached data
        // (otherwise both would say "not in your library").
        LoadCatalogButton.IsVisible = _signedIn;
        LoadCatalogButton.IsEnabled = !_busy && _signedIn;
        ScanButton.IsVisible = _signedIn;
        ScanButton.IsEnabled = !_busy && _signedIn && hasCache;
        FindByAuthorButton.IsVisible = _signedIn;
        FindByAuthorButton.IsEnabled = !_busy && _signedIn && hasCache;

        // Cache stats panel: passive info, only when we have data.
        // Renders independently of signed-in state — cached data
        // outlives sign-out.
        CacheStatsPanel.IsVisible = hasCache;
        if (hasCache && _meta is not null)
        {
            CacheStatsLabel.Text =
                $"{_meta.BookCount:N0} books · {_meta.AuthorCount:N0} authors";
            CacheSyncedAtLabel.Text = _meta.SyncedAt is { } syncedAt
                ? $"Last synced {FormatSyncedAt(syncedAt)}"
                : "Last synced (unknown)";
        }

        SignOutButton.IsVisible = _signedIn;
        SignOutButton.IsEnabled = !_busy && _signedIn;
    }

    // Relative-time formatter for the cache stats panel.
    // "just now / 5m ago / 2h ago / 2026-05-12 14:23". Kept inline
    // because it's UI-only with a single use site — promoting it to
    // a helper class would be over-engineered for one Label binding.
    private static string FormatSyncedAt(DateTime syncedUtc)
    {
        var ago = DateTime.UtcNow - syncedUtc;
        if (ago.TotalMinutes < 1) return "just now";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24) return $"{(int)ago.TotalHours}h ago";
        return syncedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
