# Requires: Az.Accounts, Az.Resources, Microsoft.Graph.Applications,
#           Microsoft.Graph.Users, SqlServer (v22+ for AAD token support).
# The script installs any missing modules to CurrentUser scope.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $TenantId,
    [Parameter(Mandatory)] [string] $SubscriptionId,
    [string] $Location = 'australiaeast',
    [string] $AppName = 'booktracker',
    [string] $EnterpriseAppName = 'Library-Patrons',
    # Optional custom hostname (e.g. books.silly.ninja). DNS records must be in
    # place first — the script prints them at the end of every run.
    [string] $CustomDomain = '',
    # Optional public IPv4 to whitelist on the SQL firewall for ad-hoc access
    # (e.g. local EF migrations). Leave blank to keep SQL fully private; the
    # only path in is then the Private Endpoint from inside the VNet.
    [string] $DevClientIp = '',
    # Region for AI services (Foundry + OpenAI). Defaults to eastus2 because
    # Claude on Foundry and a stable gpt-4o successor are not available in
    # australiaeast.
    [string] $SecondaryLocation = 'eastus2',
    # Optional Anthropic public-API key. When supplied it's stored in Key
    # Vault and exposed via a KV reference in App Settings.
    [string] $AnthropicApiKey = '',
    # Optional Trove (National Library of Australia) API key. Used as a
    # third-line ISBN lookup provider for titles Open Library and Google
    # Books don't index. Same storage pattern as AnthropicApiKey.
    [string] $TroveApiKey = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$required = @(
    'Az.Accounts',
    'Az.Resources',
    'Microsoft.Graph.Applications',
    'Microsoft.Graph.Users',
    'SqlServer'
)
foreach ($m in $required) {
    if (-not (Get-Module -ListAvailable -Name $m)) {
        Write-Host "Installing $m..."
        Install-Module $m -Scope CurrentUser -Force -AllowClobber -Repository PSGallery
    }
    Import-Module $m -ErrorAction Stop
}

Write-Host "Connecting to Azure ($TenantId / $SubscriptionId)..."
Connect-AzAccount -Tenant $TenantId -Subscription $SubscriptionId | Out-Null
Set-AzContext -Tenant $TenantId -Subscription $SubscriptionId | Out-Null

Write-Host "Connecting to Microsoft Graph..."
Connect-MgGraph -TenantId $TenantId -Scopes 'Application.ReadWrite.All','Directory.Read.All','User.Read' -NoWelcome

# ---- Resolve the signed-in user for the SQL AAD admin role -------------------
# Use Graph's /me — the Az sign-in identifier (email, B2B UPN, etc.) doesn't
# always match the directory UPN, so we let Graph tell us who we actually are.
$meResponse = Invoke-MgGraphRequest -Method GET -Uri 'https://graph.microsoft.com/v1.0/me' -ErrorAction Stop
$me = [pscustomobject]@{
    Id                = $meResponse.id
    UserPrincipalName = $meResponse.userPrincipalName
    DisplayName       = $meResponse.displayName
}
if (-not $me.Id) {
    throw "Unable to resolve current user via Microsoft Graph /me. The signed-in identity must be a user, not a service principal."
}
Write-Host "SQL AAD admin will be set to: $($me.DisplayName) <$($me.UserPrincipalName)>"

# ---- Ensure the "Library-Patrons" App Registration + SP ----------------------
Write-Host "Ensuring App Registration '$EnterpriseAppName'..."
$app = Get-MgApplication -Filter "displayName eq '$EnterpriseAppName'" -ConsistencyLevel eventual -CountVariable c | Select-Object -First 1
if (-not $app) {
    $app = New-MgApplication -DisplayName $EnterpriseAppName -SignInAudience 'AzureADMyOrg' -Web @{
        ImplicitGrantSettings = @{
            EnableIdTokenIssuance  = $true
            EnableAccessTokenIssuance = $false
        }
    }
    Write-Host "  Created App Registration, AppId=$($app.AppId)"
    Start-Sleep -Seconds 15  # replication delay before SP can be created
} else {
    Write-Host "  Found existing App Registration, AppId=$($app.AppId)"
}

$sp = Get-MgServicePrincipal -Filter "appId eq '$($app.AppId)'" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $sp) {
    $sp = New-MgServicePrincipal -AppId $app.AppId
    Write-Host "  Created Service Principal $($sp.Id)"
}

if (-not $sp.AppRoleAssignmentRequired) {
    Update-MgServicePrincipal -ServicePrincipalId $sp.Id -BodyParameter @{ appRoleAssignmentRequired = $true } | Out-Null
    Write-Host "  Set appRoleAssignmentRequired = true (only assigned users can sign in)"
}

# ---- Rotate / create a client secret for Easy Auth ---------------------------
# Scheduled rotation is handled by .github/workflows/rotate-easy-auth-secret.yml
# (every 6 months, via the booktracker-ci OIDC SP). Re-running deploy.ps1 also
# rotates — kept for first-provision and as a manual fallback.
Write-Host "Creating client secret for Easy Auth..."
$pwdCred = Add-MgApplicationPassword -ApplicationId $app.Id -PasswordCredential @{
    DisplayName = "easyauth-$(Get-Date -Format yyyyMMdd)"
    EndDateTime = (Get-Date).ToUniversalTime().AddYears(2)
}
# New-AzSubscriptionDeployment can't serialize a [SecureString] inside
# -TemplateParameterObject, so we pass the secret as a plain string. Azure
# encrypts it in transit, and the @secure() decorator on the Bicep param keeps
# it out of deployment history / logs.
$clientSecret = $pwdCred.SecretText
Write-Host "  Secret expires $($pwdCred.EndDateTime)."

# ---- Deploy the Bicep template at subscription scope -------------------------
$templateFile = Join-Path $PSScriptRoot 'main.bicep'
$deployName = "booktracker-infra-$(Get-Date -Format yyyyMMddHHmmss)"
Write-Host "Deploying Bicep ($deployName)..."

$templateParams = @{
    location            = $Location
    appName             = $AppName
    tenantId            = $TenantId
    authClientId        = $app.AppId
    authClientSecret    = $clientSecret
    sqlAadAdminObjectId = $me.Id
    sqlAadAdminLogin    = $me.UserPrincipalName
    customDomain        = $CustomDomain
    devClientIp         = $DevClientIp
    secondaryLocation   = $SecondaryLocation
    anthropicApiKey     = $AnthropicApiKey
    troveApiKey         = $TroveApiKey
}

$deployment = New-AzSubscriptionDeployment `
    -Name $deployName `
    -Location $Location `
    -TemplateFile $templateFile `
    -TemplateParameterObject $templateParams

if ($deployment.ProvisioningState -ne 'Succeeded') {
    throw "Deployment failed: $($deployment.ProvisioningState)"
}

$appUrl = $deployment.Outputs.appServiceUrl.Value
$appServiceName = $deployment.Outputs.appServiceName.Value
$sqlFqdn = $deployment.Outputs.sqlServerFqdn.Value
$sqlDb = $deployment.Outputs.sqlDatabaseName.Value
$stagingSqlDb = $deployment.Outputs.stagingSqlDatabaseName.Value
$appHost = ([Uri]$appUrl).Host
$stagingHost = $deployment.Outputs.stagingHostName.Value
$stagingSlotSqlUserName = "$appServiceName/slots/staging"

Write-Host ""
Write-Host "Infrastructure deployed."
Write-Host "  App Service: $appUrl"
Write-Host "  SQL Server:  $sqlFqdn"
Write-Host "  Prod DB:     $sqlDb"
Write-Host "  Staging DB:  $stagingSqlDb"

# ---- Ensure the Easy Auth redirect URIs are registered (prod + staging) -----
$redirects = @(
    "https://$appHost/.auth/login/aad/callback"
    "https://$stagingHost/.auth/login/aad/callback"
)
if ($CustomDomain) {
    $redirects += "https://$CustomDomain/.auth/login/aad/callback"
}
$currentUris = @($app.Web.RedirectUris)
$missing = $redirects | Where-Object { $currentUris -notcontains $_ }
if ($missing) {
    $newUris = @(($currentUris + $redirects) | Where-Object { $_ } | Select-Object -Unique)
    Update-MgApplication -ApplicationId $app.Id -Web @{
        RedirectUris = $newUris
        ImplicitGrantSettings = @{
            EnableIdTokenIssuance  = $true
            EnableAccessTokenIssuance = $false
        }
    }
    foreach ($r in $missing) { Write-Host "  Added redirect URI: $r" }
}

# ---- Grant the App Service managed identity access to the SQL DB -------------
# With AAD-only SQL, this is the only way the app can reach the database.
Write-Host "Granting App Service managed identity access to SQL..."

# SQL is reachable only via Private Endpoint by default, so we temporarily
# enable public access + add a firewall rule for this machine's IP, run the
# AAD grant, then revert. If the user passed -DevClientIp the deployment
# already left public access on, so we skip the toggle.
$sqlServerResourceName = ($sqlFqdn -split '\.')[0]
$rgName = $deployment.Outputs.resourceGroupName.Value

$sqlServer = Get-AzSqlServer -ResourceGroupName $rgName -ServerName $sqlServerResourceName
$publicAccessWas = $sqlServer.PublicNetworkAccess
if ($publicAccessWas -ne 'Enabled') {
    Write-Host "  Temporarily enabling SQL public network access..."
    Set-AzSqlServer -ResourceGroupName $rgName -ServerName $sqlServerResourceName -PublicNetworkAccess 'Enabled' | Out-Null
}

$myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 10).Trim()
$tempRuleName = "deploy-script-$([guid]::NewGuid().ToString('N').Substring(0,8))"
Write-Host "  Adding temporary SQL firewall rule for $myIp ($tempRuleName)..."
New-AzSqlServerFirewallRule `
    -ResourceGroupName $rgName `
    -ServerName $sqlServerResourceName `
    -FirewallRuleName $tempRuleName `
    -StartIpAddress $myIp `
    -EndIpAddress $myIp | Out-Null
# Server-side propagation can lag the API response by a few seconds.
Start-Sleep -Seconds 10

try {
    # Each slot has its own system-assigned managed identity, and each slot's
    # connection string is slot-sticky and points at its own DB. So each
    # identity only needs access to its slot's DB. Grant prod identity on
    # the prod DB; grant staging identity on the staging DB.
    #
    # The SQL user name for a slot is "<app-service-name>/slots/<slot-name>".
    # The prod block also drops the staging identity if it exists from a
    # pre-split deploy (when both identities were granted on the prod DB) —
    # leaving the orphan grant would mask a slot-sticky-CS regression.
    $prodGrantSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$appServiceName')
BEGIN
    CREATE USER [$appServiceName] FROM EXTERNAL PROVIDER;
    ALTER ROLE db_datareader ADD MEMBER [$appServiceName];
    ALTER ROLE db_datawriter ADD MEMBER [$appServiceName];
    ALTER ROLE db_ddladmin  ADD MEMBER [$appServiceName];
END

DROP USER IF EXISTS [$stagingSlotSqlUserName];
"@

    $stagingGrantSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$stagingSlotSqlUserName')
BEGIN
    CREATE USER [$stagingSlotSqlUserName] FROM EXTERNAL PROVIDER;
    ALTER ROLE db_datareader ADD MEMBER [$stagingSlotSqlUserName];
    ALTER ROLE db_datawriter ADD MEMBER [$stagingSlotSqlUserName];
    ALTER ROLE db_ddladmin  ADD MEMBER [$stagingSlotSqlUserName];
END
"@

    $token = (Get-AzAccessToken -ResourceUrl 'https://database.windows.net').Token
    Write-Host "  Granting prod identity on '$sqlDb'..."
    Invoke-Sqlcmd -ServerInstance $sqlFqdn -Database $sqlDb -AccessToken $token -Query $prodGrantSql -Encrypt Mandatory
    Write-Host "  Granting staging identity on '$stagingSqlDb'..."
    Invoke-Sqlcmd -ServerInstance $sqlFqdn -Database $stagingSqlDb -AccessToken $token -Query $stagingGrantSql -Encrypt Mandatory
}
finally {
    Write-Host "  Removing temporary SQL firewall rule..."
    Remove-AzSqlServerFirewallRule `
        -ResourceGroupName $rgName `
        -ServerName $sqlServerResourceName `
        -FirewallRuleName $tempRuleName `
        -Force -ErrorAction SilentlyContinue | Out-Null

    if ($publicAccessWas -ne 'Enabled') {
        Write-Host "  Restoring SQL public network access to '$publicAccessWas'..."
        Set-AzSqlServer -ResourceGroupName $rgName -ServerName $sqlServerResourceName -PublicNetworkAccess $publicAccessWas -ErrorAction SilentlyContinue | Out-Null
    }
}

Write-Host ""
Write-Host "Done."
Write-Host "  App URL: $appUrl"
if ($CustomDomain) {
    Write-Host "  Custom URL: https://$CustomDomain"
}
Write-Host ""
Write-Host "Next step — assign users/groups to the '$EnterpriseAppName' enterprise app:"
Write-Host "  https://entra.microsoft.com -> Identity -> Applications -> Enterprise applications -> $EnterpriseAppName -> Users and groups"
Write-Host "Only assigned principals will be able to sign in."

# ---- DNS records required for a custom domain (print every run) -------------
$defaultHost = $deployment.Outputs.defaultHostName.Value
$verificationId = $deployment.Outputs.customDomainVerificationId.Value
Write-Host ""
Write-Host "----"
Write-Host "To bind a custom domain (like books.silly.ninja), add these DNS records"
Write-Host "at your registrar (Gandi, Cloudflare, etc.), wait for propagation, then re-run"
Write-Host "this script with -CustomDomain <hostname>:"
Write-Host ""
Write-Host "  TXT   asuid.<subdomain>    $verificationId"
Write-Host "  CNAME <subdomain>          $defaultHost"
Write-Host ""
Write-Host "e.g. for books.silly.ninja:"
Write-Host "  TXT   asuid.books          $verificationId"
Write-Host "  CNAME books                $defaultHost"
Write-Host "----"
