using BookTracker.Mobile.Cache;

namespace BookTracker.Mobile.Theming;

/// <summary>
/// Shared async cover loader for book rows: fetches + disk-caches the cover via
/// the catalog cache (<see cref="ICatalogCache.EnsureCoverCachedAsync"/>), then
/// swaps the placeholder glyph for the image on the UI thread. Best-effort — on
/// failure the placeholder stays and the next render of the row retries.
/// </summary>
public static class Covers
{
    public static async Task LoadIntoAsync(
        ICatalogCache cache,
        IHttpClientFactory httpFactory,
        int bookId,
        Image target,
        Label placeholder)
    {
        try
        {
            var http = httpFactory.CreateClient("covers");
            var path = await cache.EnsureCoverCachedAsync(bookId, http);
            if (path is null) return;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                target.Source = ImageSource.FromFile(path);
                target.IsVisible = true;
                placeholder.IsVisible = false;
            });
        }
        catch
        {
            // Placeholder stays; a later render (revisit/scroll) retries.
        }
    }
}
