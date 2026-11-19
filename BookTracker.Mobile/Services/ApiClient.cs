using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BookTracker.Shared.Catalog;
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

    public Task<CatalogSnapshot> GetCatalogSnapshotAsync(
        DateTime? since = null, CancellationToken ct = default)
    {
        // ISO 8601 "O" format round-trips the UTC offset (the "Z"
        // suffix) so the server-side AdjustToUniversal | AssumeUniversal
        // parser pins it to UTC even though we use raw string-binding.
        // Uri.EscapeDataString handles the ":" / "." characters that
        // would otherwise need percent-encoding in a query string.
        var path = since is { } sinceUtc
            ? $"/api/catalog-snapshot?since={Uri.EscapeDataString(sinceUtc.ToUniversalTime().ToString("O"))}"
            : "/api/catalog-snapshot";
        return GetAuthorisedAsync<CatalogSnapshot>(path, "Catalog snapshot", ct);
    }

    public Task<WishlistSnapshot> GetWishlistSnapshotAsync(CancellationToken ct = default) =>
        GetAuthorisedAsync<WishlistSnapshot>("/api/wishlist-snapshot", "Wishlist snapshot", ct);

    // Sends the GET with the cached token. A 401 means the API rejected that
    // token, so the cached account is cleared (which makes SignInAsync go
    // interactive rather than hand back the same token) and the request is
    // retried exactly once with the new token. The retry is straight-line
    // code, not a loop: a second 401 throws from EnsureSuccessStatusCode, and
    // a cancelled sign-in throws from SignInAsync before any retry is sent.
    private async Task<T> GetAuthorisedAsync<T>(string path, string what, CancellationToken ct)
    {
        var token = await auth.AcquireTokenSilentAsync()
            ?? await auth.SignInAsync();

        using var response = await SendAsync(path, token, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return await ReadAsync<T>(response, what, ct);

        await auth.SignOutAsync();
        var freshToken = await auth.SignInAsync();

        using var retry = await SendAsync(path, freshToken, ct);
        return await ReadAsync<T>(retry, what, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, string what, CancellationToken ct)
    {
        response.EnsureSuccessStatusCode();
        var snapshot = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return snapshot
            ?? throw new InvalidOperationException($"{what} deserialised as null.");
    }
}
