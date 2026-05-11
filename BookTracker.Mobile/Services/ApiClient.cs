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

    public async Task<CatalogSnapshot> GetCatalogSnapshotAsync(CancellationToken ct = default)
    {
        var token = await auth.AcquireTokenSilentAsync()
            ?? await auth.SignInAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/catalog-snapshot");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var snapshot = await response.Content.ReadFromJsonAsync<CatalogSnapshot>(JsonOptions, ct);
        return snapshot
            ?? throw new InvalidOperationException("Catalog snapshot deserialised as null.");
    }
}
