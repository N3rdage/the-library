# Rotates the Easy Auth client secret on the Library-Patrons app registration
# and writes the new value to Key Vault.
#
# Called from .github/workflows/rotate-easy-auth-secret.yml on a twice-yearly
# schedule (and for manual workflow_dispatch). Also runnable locally for one-off
# rotations, provided the calling identity has the required permissions.
#
# The calling identity needs:
#   - Owner of the target app registration (for Add-MgApplicationPassword /
#     Remove-MgApplicationPassword) OR Application.ReadWrite.OwnedBy as a
#     Graph app role
#   - Key Vault Secrets Officer on the KV (for Set-AzKeyVaultSecret against
#     an RBAC-enabled vault)
#
# Both are granted to the booktracker-ci OIDC service principal by
# infra/setup-github-oidc.ps1.
#
# The rotation keeps the latest $KeepLatest passwords on the app registration
# and deletes older ones. Default 2 — so a fresh rotation never leaves the
# previously-active secret orphaned before App Service picks up the new one
# via KV reference refresh (which can take up to ~24h without a manual
# portal refresh).

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $TenantId,
    [Parameter(Mandatory)] [string] $SubscriptionId,
    [Parameter(Mandatory)] [string] $ResourceGroupName,
    [string] $AppRegistrationDisplayName = 'Library-Patrons',
    [string] $KeyVaultName = '',
    [string] $KeyVaultSecretName = 'AuthClientSecret',
    [int] $KeepLatest = 2,
    [int] $SecretLifetimeYears = 2
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ---- Discover Key Vault name if not supplied --------------------------------
# Single KV per RG by convention (matches the Bicep). Fail loudly if we find
# zero or more than one so the caller is forced to disambiguate.
if ([string]::IsNullOrEmpty($KeyVaultName)) {
    Write-Host "Discovering Key Vault in resource group '$ResourceGroupName'..."
    $kvs = Get-AzKeyVault -ResourceGroupName $ResourceGroupName
    if ($null -eq $kvs -or $kvs.Count -eq 0) {
        throw "No Key Vault found in $ResourceGroupName. Pass -KeyVaultName explicitly."
    }
    if ($kvs.Count -gt 1) {
        throw "Expected exactly one Key Vault in $ResourceGroupName, found $($kvs.Count). Pass -KeyVaultName explicitly."
    }
    $KeyVaultName = $kvs[0].VaultName
    Write-Host "  Using $KeyVaultName"
}

# ---- Connect to Microsoft Graph using the existing Azure auth session -------
# azure/login@v2 logs us in to ARM; Graph is a separate endpoint so we exchange
# the session for a Graph access token and feed it to Connect-MgGraph.
Write-Host "Connecting to Microsoft Graph..."
$graphTokenJson = az account get-access-token --resource https://graph.microsoft.com --output json | ConvertFrom-Json
if (-not $graphTokenJson.accessToken) {
    throw "Could not acquire a Microsoft Graph access token via 'az'. Ensure you ran 'azure/login@v2' / 'Connect-AzAccount' before this script."
}
$graphToken = ConvertTo-SecureString $graphTokenJson.accessToken -AsPlainText -Force
Connect-MgGraph -AccessToken $graphToken -NoWelcome

# ---- Find the target app registration ---------------------------------------
Write-Host "Finding app registration '$AppRegistrationDisplayName'..."
$app = Get-MgApplication -Filter "displayName eq '$AppRegistrationDisplayName'" -ConsistencyLevel eventual -CountVariable c | Select-Object -First 1
if (-not $app) {
    throw "App registration '$AppRegistrationDisplayName' not found in tenant $TenantId."
}
Write-Host "  ObjectId = $($app.Id), AppId = $($app.AppId)"

# ---- Add a fresh password ---------------------------------------------------
$displayName = "easyauth-$(Get-Date -Format yyyyMMdd)"
$endDate = (Get-Date).ToUniversalTime().AddYears($SecretLifetimeYears)
Write-Host "Adding password '$displayName' (expires $($endDate.ToString('yyyy-MM-dd')))..."
$newPwd = Add-MgApplicationPassword -ApplicationId $app.Id -PasswordCredential @{
    DisplayName = $displayName
    EndDateTime = $endDate
}

# ---- Write the new secret value to Key Vault --------------------------------
Write-Host "Writing new secret to Key Vault '$KeyVaultName' as '$KeyVaultSecretName'..."
$secureValue = ConvertTo-SecureString $newPwd.SecretText -AsPlainText -Force
Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name $KeyVaultSecretName -SecretValue $secureValue -Expires $endDate | Out-Null

# ---- Trim old passwords -----------------------------------------------------
# Re-fetch to include the new one, sort newest-first, keep the top $KeepLatest,
# delete the rest. A KeepLatest of 2 means the new one plus the previous-active
# one stay alive — the previous stays valid until App Service's KV reference
# refresh picks up the new value (up to ~24h).
Write-Host "Trimming old passwords (keeping latest $KeepLatest)..."
$refreshed = Get-MgApplication -ApplicationId $app.Id
$sorted = $refreshed.PasswordCredentials | Sort-Object StartDateTime -Descending
$toKeep = $sorted | Select-Object -First $KeepLatest
$toDelete = $sorted | Select-Object -Skip $KeepLatest

foreach ($pwd in $toDelete) {
    Write-Host "  Removing '$($pwd.DisplayName)' (start $($pwd.StartDateTime.ToString('yyyy-MM-dd')))"
    Remove-MgApplicationPassword -ApplicationId $app.Id -KeyId $pwd.KeyId
}

Write-Host ""
Write-Host "Passwords retained:"
foreach ($pwd in $toKeep) {
    Write-Host "  $($pwd.DisplayName): start $($pwd.StartDateTime.ToString('yyyy-MM-dd')), expires $($pwd.EndDateTime.ToString('yyyy-MM-dd'))"
}

Write-Host ""
Write-Host "Done. App Service will pick up the new secret via the Key Vault reference within ~24h."
Write-Host "Force an immediate refresh via Azure Portal -> App Service -> Settings -> Environment variables -> 'Refresh Key Vault references'."
