using BookTracker.Mobile.Pages;
using BookTracker.Mobile.Theming;

namespace BookTracker.Mobile;

public partial class AppShell : Shell
{
    // Pages are injected (not DataTemplate-instantiated) so they get their DI
    // dependencies. Content is set eagerly — three tab pages is cheap, and
    // each still loads its data in OnAppearing when its tab is first shown.
    public AppShell(MainPage find, WishlistPage wishlist, SeriesGapsPage gaps)
    {
        InitializeComponent();

        FindTab.Content = find;
        WishlistTab.Content = wishlist;
        GapsTab.Content = gaps;

        // Tab icons — Shell tints them with TabBarForeground (active) /
        // TabBarUnselected (inactive), so no explicit colour here.
        FindTab.Icon = TabIcon(TablerIcons.Search);
        WishlistTab.Icon = TabIcon(TablerIcons.Star);
        GapsTab.Icon = TabIcon(TablerIcons.Books);
    }

    private static FontImageSource TabIcon(string glyph) =>
        new() { FontFamily = "Tabler", Glyph = glyph, Size = 22 };
}
