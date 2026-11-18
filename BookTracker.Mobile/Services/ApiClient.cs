using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BookTracker.Shared.Catalog;
using BookTracker.Shared.Online;
using BookTracker.Shared.Wishlist;

namespace BookTracker.Mobile.Services;

public class ApiClient(HttpClient http, IAuthService auth) : IApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // BookTracker.Web's Minimal API serialises with the default
        // PascalCase property names; the Shared records match.
        PropertyNameCaseInsensitive = true,
    };

    public async Task<CatalogSnapshot> GetCatalogSnapshotAsync(
        DateTime? since = null, CancellationToken ct = default)
    {
        var token = await auth.AcquireTokenSilentAsync()
            ?? await auth.SignInAsync();

        // ISO 8601 "O" format round-trips the UTC offset (the "Z"
        // suffix) so the server-side AdjustToUniversal | AssumeUniversal
        // parser pins it to UTC even though we use raw string-binding.
        // Uri.EscapeDataString handles the ":" / "." characters that
        // would otherwise need percent-encoding in a query string.
        var path = since is { } sinceUtc
            ? $"/api/catalog-snapshot?since={Uri.EscapeDataString(sinceUtc.ToUniversalTime().ToString("O"))}"
            : "/api/catalog-snapshot";
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var snapshot = await response.Content.ReadFromJsonAsync<CatalogSnapshot>(JsonOptions, ct);
        return snapshot
            ?? throw new InvalidOperationException("Catalog snapshot deserialised as null.");
    }

    public async Task<WishlistSnapshot> GetWishlistSnapshotAsync(CancellationToken ct = default)
    {
        var token = await auth.AcquireTokenSilentAsync()
            ?? await auth.SignInAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/wishlist-snapshot");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var snapshot = await response.Content.ReadFromJsonAsync<WishlistSnapshot>(JsonOptions, ct);
        return snapshot
            ?? throw new InvalidOperationException("Wishlist snapshot deserialised as null.");
    }

    public async Task<OnlineIsbnLookup?> LookupIsbnOnlineAsync(string isbn, CancellationToken ct = default)
    {
        try
        {
            var token = await auth.AcquireTokenSilentAsync()
                ?? await auth.SignInAsync();

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/online/isbn/{Uri.EscapeDataString(isbn)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, ct);
            // 404 is the endpoint's "no provider knows this ISBN"; 400 and
            // 5xx are failures. Either way the page keeps its offline answer.
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadFromJsonAsync<OnlineIsbnLookup>(JsonOptions, ct);
        }
        catch (Exception)
        {
            // Sign-in cancelled, no network, timeout, bad JSON: the online
            // answer is extra on top of the cache answer already on screen, so
            // a failure means "no online answer" rather than an error. Not
            // logged: the Bookshelf services carry no logger (SyncService
            // swallows the same way), and the page shows the offline answer.
            return null;
        }
    }
}
