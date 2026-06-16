using BookTracker.Mobile.Pages;
using BookTracker.Mobile.Services;
using BookTracker.Mobile.Theming;

namespace BookTracker.Mobile;

public partial class AppShell : Shell
{
    private readonly ISyncService _sync;
    private bool _autoSyncDone;

    // Pages are injected (not DataTemplate-instantiated) so they get their DI
    // dependencies. Content is set eagerly — three tab pages is cheap, and
    // each still loads its data in OnAppearing when its tab is first shown.
    public AppShell(ISyncService sync, FindPage find, WishlistPage wishlist, SeriesGapsPage gaps)
    {
        InitializeComponent();
        _sync = sync;

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
        await _sync.InitAsync();
        await _sync.RefreshSignInAsync();
        await _sync.RefreshMetaAsync();
        if (_sync.IsSignedIn && _sync.IsOnline)
        {
            try { await _sync.SyncCatalogAsync(); }
            catch { /* best-effort on launch — the status sheet shows errors on manual refresh */ }
        }
    }

    private static FontImageSource TabIcon(string glyph) =>
        new() { FontFamily = "Tabler", Glyph = glyph, Size = 22 };
}
