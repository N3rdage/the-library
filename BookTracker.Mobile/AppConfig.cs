namespace BookTracker.Mobile;

// AAD / API configuration. These are NOT secrets — tenant IDs and
// client IDs are discoverable from any token AAD issues — but they're
// project-specific so leave them committed in the repo and update when
// the underlying app registrations change.
//
// Drew fills these in before the first build. See
// infra/README.md > "Mobile companion — AAD setup" for where each
// value comes from.
public static class AppConfig
{
    /// <summary>Entra tenant ID (GUID). Same value used for `-TenantId`
    /// in deploy.ps1.</summary>
    public const string TenantId = "REPLACE_WITH_TENANT_ID";

    /// <summary>Application (client) ID of the BookTracker Mobile app
    /// registration (the new native-client one from step 2 of the
    /// AAD-setup runbook).</summary>
    public const string MobileClientId = "REPLACE_WITH_MOBILE_CLIENT_ID";

    /// <summary>Full scope identifier the mobile app requests from
    /// AAD. Resolves to a token whose `aud` claim is
    /// `api://&lt;authClientId&gt;` — matches the existing
    /// validation.allowedAudiences entry on the App Service, so no
    /// Bicep change was needed.</summary>
    public const string ApiScope = "api://REPLACE_WITH_AUTH_CLIENT_ID/access_as_user";

    /// <summary>Base URL of the BookTracker.Web API. Custom domain
    /// preferred over the *.azurewebsites.net default so the URL is
    /// stable across slot moves and platform changes.</summary>
    public const string ApiBaseUrl = "https://books.silly.ninja";

    /// <summary>Android package name. Matches the &lt;ApplicationId&gt;
    /// in BookTracker.Mobile.csproj and the host in the msauth://
    /// redirect URI (AAD app reg + AndroidManifest intent-filter).
    /// Three places stay in lockstep.</summary>
    public const string AndroidPackageName = "com.thelibrary.mobile";
}
