using BookTracker.Mobile.Pages;
using BookTracker.Mobile.Services;

namespace BookTracker.Mobile;

// Transitional Find tab. Auth + catalog-sync moved to ISyncService + the
// status sheet (PR: status/auth). This page is now a thin view: the search
// entry points + a sync chip. PR(next) replaces it with the unified FindPage.
public partial class MainPage : ContentPage
{
    private readonly ISyncService _sync;

    public MainPage(ISyncService sync)
    {
        InitializeComponent();
        _sync = sync;
        // Re-render when sign-in / connectivity / cache meta changes (e.g.
        // after a sync from the status sheet enables the search buttons).
        _sync.StateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshUi);
        RefreshUi();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _sync.InitAsync();        // idempotent — AppShell already ran it
        await _sync.RefreshMetaAsync(); // reflect any sync that happened elsewhere
        RefreshUi();
    }

    private async void OnScanClicked(object? sender, EventArgs e) => await PushAsync<ScanPage>();
    private async void OnFindByAuthorClicked(object? sender, EventArgs e) => await PushAsync<AuthorSearchPage>();
    private async void OnFindByTitleClicked(object? sender, EventArgs e) => await PushAsync<TitleSearchPage>();

    private async void OnSyncChipTapped(object? sender, TappedEventArgs e) =>
        await StatusSheetPage.OpenAsync(Navigation);

    private async Task PushAsync<TPage>() where TPage : Page
    {
        // Resolve transient pages from DI (fresh per push within the Find tab).
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("ServiceProvider not available.");
        var page = services.GetService(typeof(TPage)) as TPage
            ?? throw new InvalidOperationException($"{typeof(TPage).Name} not registered.");
        await Navigation.PushAsync(page);
    }

    private void RefreshUi()
    {
        // Offline-first: search the cache whether or not you're signed in —
        // the buttons need cached data, not a session.
        var hasCache = _sync.Meta is not null;
        ScanButton.IsEnabled = hasCache;
        FindByAuthorButton.IsEnabled = hasCache;
        FindByTitleButton.IsEnabled = hasCache;

        SyncChipLabel.Text = _sync.IsOnline
            ? $"⟳ {SyncService.FormatAge(_sync.Meta?.SyncedAt)}"
            : "⚠ offline";

        StatusLabel.Text = hasCache
            ? ""
            : _sync.IsSignedIn
                ? "No catalog cached yet — tap the sync chip, then Refresh now."
                : "No catalog cached yet — tap the sync chip to sign in and load.";
    }
}
