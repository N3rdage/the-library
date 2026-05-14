using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BookTracker.Shared.Catalog;

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
}
