using BookTracker.Mobile.Services;

namespace BookTracker.Mobile;

public partial class MainPage : ContentPage
{
    private readonly IAuthService _auth;
    private readonly IApiClient _api;

    private bool _signedIn;
    private bool _busy;

    public MainPage(IAuthService auth, IApiClient api)
    {
        InitializeComponent();
        _auth = auth;
        _api = api;
        RefreshUi();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // If a cached account already exists, surface the signed-in
        // state on first render so the user doesn't have to tap
        // Sign in just to find out they're already signed in.
        if (await _auth.IsSignedInAsync())
        {
            _signedIn = true;
            StatusLabel.Text = "Signed in. Tap to load the catalog.";
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
            return
                $"Catalog loaded ✓\n" +
                $"Version: {snapshot.Version}\n" +
                $"Synced at: {snapshot.SyncedAt:u}\n" +
                $"Books: {snapshot.Books.Count}\n" +
                $"Authors: {snapshot.Authors.Count}\n" +
                $"Series: {snapshot.Series.Count}";
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
    // any exception as the new status so a stack trace doesn't dump
    // to the user.
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
            StatusLabel.Text = $"Failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            RefreshUi();
        }
    }

    private void RefreshUi()
    {
        Busy.IsRunning = _busy;
        SignInButton.IsEnabled = !_busy && !_signedIn;
        LoadCatalogButton.IsEnabled = !_busy && _signedIn;
        SignOutButton.IsEnabled = !_busy && _signedIn;
    }
}
