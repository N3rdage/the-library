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
            // Disable broker (Microsoft Authenticator). If installed,
            // it would try to handle the auth flow itself — different
            // redirect URI requirements + a separate broker-redirect
            // registration in AAD. For PR 3 we stick to the
            // Chrome-Custom-Tab path which is simpler to debug.
            .WithBroker(false)
            // Redirect URI is the RAW base64 SHA-1 of the keystore —
            // no URL encoding. Microsoft's official Xamarin Android
            // sample uses this form, and Android's intent-filter
            // path-matching decodes %XX before matching, so the raw
            // form is what actually matches at runtime. Same value
            // must appear (raw) in:
            //   - android:path on the BrowserTabActivity in
            //     Platforms/Android/AndroidManifest.xml
            //   - BookTracker Mobile AAD app reg's redirect URIs
            // To recompute when the keystore changes:
            //   keytool -printcert -jarfile <signed.apk>
            // then base64-encode the raw SHA-1 bytes (no URL encoding).
            .WithRedirectUri($"msauth://{AppConfig.AndroidPackageName}/dMdWTcw8hf5TVPEfRB+m0JPyUDs=")
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
