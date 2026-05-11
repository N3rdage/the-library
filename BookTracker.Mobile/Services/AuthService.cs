using Microsoft.Identity.Client;

namespace BookTracker.Mobile.Services;

// MSAL.NET wrapper for AAD interactive sign-in on Android. The
// PublicClientApplication is built once at construction; token cache
// persists across app launches via MSAL's built-in cache (Keystore-
// backed on Android). See docs/mobile-app-design.md > Authentication.
public class AuthService : IAuthService
{
    private static readonly string[] Scopes = [AppConfig.ApiScope];

    private readonly IPublicClientApplication _pca;

    public AuthService()
    {
        _pca = PublicClientApplicationBuilder
            .Create(AppConfig.MobileClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, AppConfig.TenantId)
            // Required on Android — MSAL needs the current Activity
            // to launch the Chrome Custom Tab. Microsoft.Maui.
            // ApplicationModel.Platform.CurrentActivity is the
            // MAUI-supported way to get it from any thread.
            .WithParentActivityOrWindow(() =>
                Microsoft.Maui.ApplicationModel.Platform.CurrentActivity)
            // Signature hash is the URL-encoded base64 SHA-1 of the
            // debug keystore (Drew's machine, 2026-05-11). When the
            // keystore changes — different dev machine, release-signed
            // build, CI signing — recompute via:
            //   keytool -printcert -jarfile <signed.apk>
            // then base64-encode the raw SHA-1 bytes + URL-encode.
            // The same value must be registered as a redirect URI on
            // the BookTracker Mobile AAD app reg, and the
            // android:path on the BrowserTabActivity in
            // Platforms/Android/AndroidManifest.xml.
            .WithRedirectUri($"msauth://{AppConfig.AndroidPackageName}/dMdWTcw8hf5TVPEfRB%2Bm0JPyUDs%3D")
            .Build();
    }

    public async Task<string> SignInAsync()
    {
        // Try silent first — if the user signed in recently, the
        // cached refresh token gets us a new access token without UI.
        var existing = await _pca.GetAccountsAsync();
        var account = existing.FirstOrDefault();

        if (account is not null)
        {
            try
            {
                var silent = await _pca
                    .AcquireTokenSilent(Scopes, account)
                    .ExecuteAsync();
                return silent.AccessToken;
            }
            catch (MsalUiRequiredException)
            {
                // Refresh token expired / revoked — fall through to
                // interactive flow.
            }
        }

        var result = await _pca
            .AcquireTokenInteractive(Scopes)
            .ExecuteAsync();
        return result.AccessToken;
    }

    public async Task<string?> AcquireTokenSilentAsync()
    {
        var existing = await _pca.GetAccountsAsync();
        var account = existing.FirstOrDefault();
        if (account is null) return null;

        try
        {
            var result = await _pca
                .AcquireTokenSilent(Scopes, account)
                .ExecuteAsync();
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            return null;
        }
    }

    public async Task SignOutAsync()
    {
        var accounts = await _pca.GetAccountsAsync();
        foreach (var account in accounts.ToList())
        {
            await _pca.RemoveAsync(account);
        }
    }

    public async Task<bool> IsSignedInAsync()
    {
        var accounts = await _pca.GetAccountsAsync();
        return accounts.Any();
    }
}
