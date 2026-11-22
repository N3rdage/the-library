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
#   - Permission to write a secret through Azure Resource Manager
#     (Microsoft.KeyVault/vaults/secrets/write, which Contributor on the
#     resource group includes). The vault denies public network access, so
#     the secret is written through the management API, the same route the
#     Bicep deployment uses, and never through the vault's own endpoint,
#     which a GitHub runner cannot reach.
#
# Both are granted to the booktracker-ci OIDC service principal by
# infra/setup-github-oidc.ps1.
#
# Nothing identifying is printed: the workflow's log is public, so the script never
# writes the vault's name, the app registration's ids, the tenant or a resource path,
# only the passwords' display names (easyauth-<date>) and counts.
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

# ---- Helper: remove a password, waiting out Entra's write contention ---------
# Entra answers 409 Directory_ConcurrencyViolation ("concurrent requests being
# made to the tenant; wait briefly and retry") when an app registration is
# changed again within a second or two of its last change, which is exactly
# what this script does: it adds a password, then removes others. So every
# removal waits and retries on that answer; any other error stops at once.
function Remove-AppPasswordWithRetry {
    param(
        [Parameter(Mandatory)] [string] $ApplicationId,
        [Parameter(Mandatory)] [string] $KeyId,
        [int] $MaxAttempts = 6
    )
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            Remove-MgApplicationPassword -ApplicationId $ApplicationId -KeyId $KeyId
            return
        }
        catch {
            $text = "$_"
            $isContention = $text -match 'ConcurrencyViolation' -or $text -match 'concurrent requests' -or $text -match '409'
            if (-not $isContention -or $attempt -eq $MaxAttempts) { throw }
            $wait = 5 * $attempt
            Write-Host "    Entra is still settling (attempt $attempt of $MaxAttempts); waiting $wait s..."
            Start-Sleep -Seconds $wait
        }
    }
}

# ---- Discover Key Vault name if not supplied --------------------------------
# Single KV per RG by convention (matches the Bicep). Fail loudly if we find
# zero or more than one so the caller is forced to disambiguate.
if ([string]::IsNullOrEmpty($KeyVaultName)) {
    Write-Host "Discovering the Key Vault in the resource group..."
    $kvs = Get-AzKeyVault -ResourceGroupName $ResourceGroupName
    if ($null -eq $kvs -or $kvs.Count -eq 0) {
        throw "No Key Vault found in the resource group. Pass -KeyVaultName explicitly."
    }
    if ($kvs.Count -gt 1) {
        throw "Expected exactly one Key Vault in the resource group, found $($kvs.Count). Pass -KeyVaultName explicitly."
    }
    $KeyVaultName = $kvs[0].VaultName
    Write-Host "  Found it."
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
Write-Host "Finding the app registration..."
$app = Get-MgApplication -Filter "displayName eq '$AppRegistrationDisplayName'" -ConsistencyLevel eventual -CountVariable c | Select-Object -First 1
if (-not $app) {
    throw "The app registration was not found (check the AppRegistrationDisplayName parameter)."
}
Write-Host "  Found it."

# ---- Add a fresh password ---------------------------------------------------
$displayName = "easyauth-$(Get-Date -Format yyyyMMdd)"
$endDate = (Get-Date).ToUniversalTime().AddYears($SecretLifetimeYears)
Write-Host "Adding password '$displayName' (expires $($endDate.ToString('yyyy-MM-dd')))..."
$newPwd = Add-MgApplicationPassword -ApplicationId $app.Id -PasswordCredential @{
    DisplayName = $displayName
    EndDateTime = $endDate
}

# ---- Write the new secret value to Key Vault --------------------------------
# Through Azure Resource Manager, not the vault's data plane (see the header).
# If the write fails for any reason, the password just added is removed again
# before the script stops: a password nobody holds the value of must never be
# left on the app registration, because the trim below keeps the newest
# $KeepLatest and would then delete the one the site is actually using.
Write-Host "Writing the new secret to Key Vault as '$KeyVaultSecretName' (via Azure Resource Manager)..."
try {
    $secretPath = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.KeyVault/vaults/$KeyVaultName/secrets/${KeyVaultSecretName}?api-version=2023-07-01"
    $payload = @{
        properties = @{
            value      = $newPwd.SecretText
            attributes = @{
                enabled = $true
                exp     = [DateTimeOffset]::new($endDate).ToUnixTimeSeconds()
            }
        }
    } | ConvertTo-Json -Depth 5 -Compress
    $response = Invoke-AzRestMethod -Method PUT -Path $secretPath -Payload $payload
    if ($response.StatusCode -notin 200, 201) {
        # Only the status and the error code: the body names the vault and the resource path.
        $code = 'unknown'
        try { $code = ($response.Content | ConvertFrom-Json).error.code } catch { }
        throw "Key Vault secret write returned HTTP $($response.StatusCode) ($code)."
    }
}
catch {
    $writeError = $_
    Write-Host "Key Vault write failed; removing the password just added ('$displayName') so nothing is left behind..."
    try {
        Remove-AppPasswordWithRetry -ApplicationId $app.Id -KeyId $newPwd.KeyId
        Write-Host "  Removed."
    }
    catch {
        Write-Host "::error::Could not remove password '$displayName'. Delete it by hand on the app registration (Certificates & secrets) BEFORE running this again."
    }
    throw $writeError
}

# ---- Trim old passwords, one at a time ---------------------------------------
# Keep the password just added plus the newest ($KeepLatest - 1) others; remove the
# rest one by one, and confirm each removal before the next. The new password is
# kept by its KeyId, not by sorting: Graph can return the application without it
# for a few seconds after the add, and a sort over that list would keep the two
# older passwords and leave three behind (the run of 19 Nov 2026 did exactly that).
function Get-AppPasswords {
    param([Parameter(Mandatory)] [string] $ApplicationId)
    @((Get-MgApplication -ApplicationId $ApplicationId).PasswordCredentials)
}

function Wait-ForPasswordState {
    # Polls until the predicate holds for the application's passwords; returns them.
    param(
        [Parameter(Mandatory)] [string] $ApplicationId,
        [Parameter(Mandatory)] [scriptblock] $Until,
        [Parameter(Mandatory)] [string] $What,
        [int] $MaxAttempts = 12
    )
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $passwords = Get-AppPasswords -ApplicationId $ApplicationId
        if (& $Until $passwords) { return $passwords }
        Start-Sleep -Seconds 5
    }
    throw "Timed out after $($MaxAttempts * 5) s waiting for: $What."
}

Write-Host "Trimming old passwords (keeping the new one and the newest $($KeepLatest - 1) other)..."
$current = Wait-ForPasswordState -ApplicationId $app.Id -What "the new password to appear" -Until {
    param($p) $p.KeyId -contains $newPwd.KeyId
}
$others = $current | Where-Object { $_.KeyId -ne $newPwd.KeyId } | Sort-Object StartDateTime -Descending
$toDelete = @($others | Select-Object -Skip ([Math]::Max($KeepLatest - 1, 0)))
$keepIds = @($newPwd.KeyId) + @($others | Select-Object -First ([Math]::Max($KeepLatest - 1, 0)) | ForEach-Object KeyId)

$removed = 0
foreach ($cred in $toDelete) {
    Write-Host "  Removing '$($cred.DisplayName)' (start $($cred.StartDateTime.ToString('yyyy-MM-dd')))..."
    Remove-AppPasswordWithRetry -ApplicationId $app.Id -KeyId $cred.KeyId
    $keyId = $cred.KeyId
    Wait-ForPasswordState -ApplicationId $app.Id -What "'$($cred.DisplayName)' to be gone" -Until {
        param($p) $p.KeyId -notcontains $keyId
    } | Out-Null
    $removed++
    Write-Host "    Confirmed removed."
}

# The end state must be exactly the passwords meant to stay; anything else fails the run.
$final = Get-AppPasswords -ApplicationId $app.Id
$unexpected = @($final | Where-Object { $keepIds -notcontains $_.KeyId })
$missing = @($keepIds | Where-Object { $final.KeyId -notcontains $_ })
if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
    $names = ($unexpected | ForEach-Object DisplayName) -join ', '
    throw "After trimming, the app registration holds $($final.Count) password(s), expected $($keepIds.Count). Unexpected: $names. Missing: $($missing.Count)."
}

Write-Host ""
Write-Host "Removed $removed; $($final.Count) retained:"
foreach ($cred in ($final | Sort-Object StartDateTime -Descending)) {
    Write-Host "  $($cred.DisplayName): start $($cred.StartDateTime.ToString('yyyy-MM-dd')), expires $($cred.EndDateTime.ToString('yyyy-MM-dd'))"
}

Write-Host ""
Write-Host "Done. App Service will pick up the new secret via the Key Vault reference within ~24h."
Write-Host "Force an immediate refresh via Azure Portal -> App Service -> Settings -> Environment variables -> 'Refresh Key Vault references'."
