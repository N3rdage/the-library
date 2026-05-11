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

        // If a cached account already exists, surface the signed-in
        // state on first render so the user doesn't have to tap
        // Sign in just to find out they're already signed in. If
        // we have catalog meta from a previous run, surface that too.
        if (await _auth.IsSignedInAsync())
        {
            _signedIn = true;
            var meta = await _cache.GetMetaAsync();
            StatusLabel.Text = meta is null
                ? "Signed in. Tap to load the catalog."
                : $"Signed in. Cached catalog: {meta.BookCount} books, {meta.AuthorCount} authors (synced {meta.SyncedAt:u}).";
            RefreshUi();
        }
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync("Signing in…", async () =>
        {
            await _auth.SignInAsync();
            _signedIn = true;
            return "Signed in. Tap to load the catalog.";
        });
    }

    private async void OnLoadCatalogClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync("Loading catalog…", async () =>
        {
            var snapshot = await _api.GetCatalogSnapshotAsync();
            // Fetch + populate the SQLite cache in the same tap so we
            // can confirm the cache plumbing works end-to-end on a
            // real device. The cache populate is atomic so a partial
            // populate can't leave the DB half-shrunken.
            await _cache.PopulateAsync(snapshot);

            // ?? 0 on each Count — a server that hasn't been redeployed
            // since Mobile PR 1 merged won't include the `series` array
            // in the response, so snapshot.Series deserialises as null
            // (which was the original NRE). Once prod catches up these
            // all stay non-null.
            return
                $"Catalog loaded + cached ✓\n" +
                $"Version: {snapshot.Version ?? "(none)"}\n" +
                $"Synced at: {snapshot.SyncedAt:u}\n" +
                $"Books: {snapshot.Books?.Count ?? 0}\n" +
                $"Authors: {snapshot.Authors?.Count ?? 0}\n" +
                $"Series: {snapshot.Series?.Count ?? 0}" +
                (snapshot.Series is null ? " (server lacks /series field — redeploy Web)" : "");
        });
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync("Signing out…", async () =>
        {
            await _auth.SignOutAsync();
            _signedIn = false;
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

    private void RefreshUi()
    {
        Busy.IsRunning = _busy;
        SignInButton.IsEnabled = !_busy && !_signedIn;
        LoadCatalogButton.IsEnabled = !_busy && _signedIn;
        SignOutButton.IsEnabled = !_busy && _signedIn;
        // Scan only makes sense when the cache has been populated —
        // gate on the cache having a syncedAt rather than signed-in
        // alone. For PR 5 simplicity we use signed-in as a proxy;
        // if the cache is empty the scan page will just say
        // "Not in your library" for everything, which is correct.
        ScanButton.IsEnabled = !_busy && _signedIn;
    }
}
