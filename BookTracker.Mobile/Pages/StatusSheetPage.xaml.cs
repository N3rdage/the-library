using BookTracker.Mobile.Services;
using BookTracker.Mobile.Theming;

namespace BookTracker.Mobile.Pages;

public partial class StatusSheetPage : ContentPage
{
    private readonly ISyncService _sync;
    private bool _busy;

    public StatusSheetPage(ISyncService sync)
    {
        InitializeComponent();
        _sync = sync;
        BuildFooter.Text = BuildInfo.DisplayString;
    }

    /// <summary>Resolves the sheet from DI and presents it modally — the single
    /// entry point every tab's sync affordance calls.</summary>
    public static async Task OpenAsync(INavigation nav)
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("ServiceProvider not available.");
        var sheet = services.GetService(typeof(StatusSheetPage)) as StatusSheetPage
            ?? throw new InvalidOperationException("StatusSheetPage not registered.");
        await nav.PushModalAsync(sheet);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // async void — an unhandled throw (e.g. MSAL on RefreshSignInAsync) would
        // crash the app, so guard and render whatever state we have.
        try
        {
            await _sync.RefreshSignInAsync();
            await _sync.RefreshMetaAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Couldn't refresh status: {ex.GetType().Name}";
        }
        Render();
    }

    private void Render()
    {
        Busy.IsRunning = _busy;

        ConnectivityLabel.Text = _sync.IsOnline ? "● Online" : "● Offline";
        ConnectivityLabel.SetThemeColor(Label.TextColorProperty,
            _sync.IsOnline ? "OnlineTxL" : "MissTagTxL",
            _sync.IsOnline ? "OnlineTxD" : "MissTagTxD");

        var meta = _sync.Meta;
        if (meta is not null)
        {
            StatsLabel.Text = $"{meta.BookCount:N0} books · {meta.AuthorCount:N0} authors";
            SyncedAtLabel.Text = $"Last synced {SyncService.FormatAge(meta.SyncedAt)}";
        }
        else
        {
            StatsLabel.Text = "No catalog cached yet.";
            SyncedAtLabel.Text = _sync.IsSignedIn
                ? "Tap Refresh now to load your library."
                : "Sign in, then Refresh, to load your library.";
        }

        // Refresh needs an authenticated session + connectivity.
        RefreshButton.IsEnabled = !_busy && _sync.IsSignedIn && _sync.IsOnline;
        AuthButton.Text = _sync.IsSignedIn ? "Sign out" : "Sign in";
        AuthButton.IsEnabled = !_busy;
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) =>
        await RunBusyAsync("Syncing…", () => _sync.SyncCatalogAsync());

    private async void OnAuthClicked(object? sender, EventArgs e) =>
        await RunBusyAsync(_sync.IsSignedIn ? "Signing out…" : "Signing in…", async () =>
        {
            if (_sync.IsSignedIn)
            {
                await _sync.SignOutAsync();
                return "Signed out. Cached data is kept.";
            }
            await _sync.SignInAsync();
            return _sync.Meta is null
                ? "Signed in. Tap Refresh now to load your library."
                : "Signed in.";
        });

    private async void OnCloseClicked(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();

    // Spinner up + buttons disabled while the action runs, then render the
    // returned status line. Mirrors the old MainPage.RunBusyAsync diagnostics.
    private async Task RunBusyAsync(string busyStatus, Func<Task<string>> action)
    {
        _busy = true;
        StatusLabel.Text = busyStatus;
        Render();
        try
        {
            StatusLabel.Text = await action();
        }
        catch (Exception ex)
        {
            var topFrame = ex.StackTrace?
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim() ?? "(no stack)";
            StatusLabel.Text = $"Failed: {ex.GetType().Name}\n{ex.Message}\n→ {topFrame}";
        }
        finally
        {
            _busy = false;
            Render();
        }
    }
}
