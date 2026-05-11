namespace BookTracker.Mobile.Services;

// Thin contract over MSAL.NET. The MAUI app talks to IAuthService
// rather than PublicClientApplication directly so testing + future
// auth-provider swaps stay easy.
public interface IAuthService
{
    /// <summary>Triggers MSAL's interactive sign-in via Chrome Custom
    /// Tabs. Caches the resulting token in SecureStorage (Keystore-
    /// backed on Android). Returns the access token on success.</summary>
    Task<string> SignInAsync();

    /// <summary>Silently acquires a fresh access token from the
    /// cached account. Returns null if no cached account exists or
    /// the refresh path fails — caller falls back to SignInAsync.</summary>
    Task<string?> AcquireTokenSilentAsync();

    /// <summary>Clears the cached account from MSAL's token cache.
    /// User has to re-authenticate next time.</summary>
    Task SignOutAsync();

    /// <summary>True when a cached account exists. Doesn't validate
    /// the token — use AcquireTokenSilentAsync for that.</summary>
    Task<bool> IsSignedInAsync();
}
