using BookTracker.Mobile.Pages;
using BookTracker.Mobile.Services;
using BookTracker.Mobile.Theming;
using Microsoft.Extensions.Logging;

namespace BookTracker.Mobile;

public partial class AppShell : Shell
{
    private readonly ISyncService _sync;
    private readonly ILogger<AppShell> _logger;
    private bool _autoSyncDone;

    // Pages are injected (not DataTemplate-instantiated) so they get their DI
    // dependencies. Content is set eagerly — three tab pages is cheap, and
    // each still loads its data in OnAppearing when its tab is first shown.
    public AppShell(ISyncService sync, ILogger<AppShell> logger,
        FindPage find, WishlistPage wishlist, SeriesGapsPage gaps)
    {
        InitializeComponent();
        _sync = sync;
        _logger = logger;

        FindTab.Content = find;
        WishlistTab.Content = wishlist;
        GapsTab.Content = gaps;

        // Tab icons — Shell tints them with TabBarForeground (active) /
        // TabBarUnselected (inactive), so no explicit colour here.
        FindTab.Icon = TabIcon(TablerIcons.Search);
        WishlistTab.Icon = TabIcon(TablerIcons.Star);
        GapsTab.Icon = TabIcon(TablerIcons.Books);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_autoSyncDone) return; // once per launch, not on every resume
        _autoSyncDone = true;

        // Open the cache before any tab reads it, then auto-sync silently when
        // we're online + already signed in (the decided default). A signed-out
        // or offline user just gets the cached data — no prompt, no block.
        // async void — a throw escaping here would crash the app, so a failure
        // to open the cache or read the sign-in state is logged and shown.
        try
        {
            await _sync.InitAsync();
            await _sync.RefreshSignInAsync();
            await _sync.RefreshMetaAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bookshelf startup failed.");
            await DisplayAlertAsync("Startup failed",
                $"{ex.GetType().Name}: {ex.Message}", "OK");
            return;
        }
        if (_sync.IsSignedIn && _sync.IsOnline)
        {
            try { await _sync.SyncCatalogAsync(); }
            catch { /* best-effort on launch — the status sheet shows errors on manual refresh */ }
        }
    }

    private static FontImageSource TabIcon(string glyph) =>
        new() { FontFamily = "Tabler", Glyph = glyph, Size = 22 };
}
