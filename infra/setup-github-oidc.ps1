# One-time setup for GitHub Actions → Azure OIDC.
# Creates an Entra app registration + service principal scoped to this repo,
# adds federated identity credentials (no secrets needed), assigns
# Contributor on the resource group, and prints the GitHub repository
# variables you need to configure.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $TenantId,
    [Parameter(Mandatory)] [string] $SubscriptionId,
    [Parameter(Mandatory)] [string] $GitHubOrg,
    [Parameter(Mandatory)] [string] $GitHubRepo,
    [string] $MainBranch = 'main',
    [string] $CiAppName = 'booktracker-ci',
    [string] $ResourceGroupName = 'rg-booktracker-prod',
    [string] $SlotName = 'staging',
    # The Easy Auth app registration that rotate-easy-auth-secret.yml manages.
    # Must already exist (created by deploy.ps1); owner + Graph permissions are
    # granted here so the CI SP can call Add-/Remove-MgApplicationPassword on it.
    [string] $EasyAuthAppRegName = 'Library-Patrons'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$required = @('Az.Accounts','Az.Resources','Microsoft.Graph.Applications')
foreach ($m in $required) {
    if (-not (Get-Module -ListAvailable -Name $m)) {
        Write-Host "Installing $m..."
        Install-Module $m -Scope CurrentUser -Force -AllowClobber -Repository PSGallery
    }
    Import-Module $m -ErrorAction Stop
}

Connect-AzAccount -Tenant $TenantId -Subscription $SubscriptionId | Out-Null
Set-AzContext -Tenant $TenantId -Subscription $SubscriptionId | Out-Null
Connect-MgGraph -TenantId $TenantId -Scopes 'Application.ReadWrite.All','Directory.Read.All','AppRoleAssignment.ReadWrite.All' -NoWelcome

# ---- Ensure the CI app registration + service principal ---------------------
Write-Host "Ensuring App Registration '$CiAppName'..."
$app = Get-MgApplication -Filter "displayName eq '$CiAppName'" -ConsistencyLevel eventual -CountVariable c | Select-Object -First 1
if (-not $app) {
    $app = New-MgApplication -DisplayName $CiAppName -SignInAudience 'AzureADMyOrg'
    Write-Host "  Created App Registration, AppId=$($app.AppId)"
    Start-Sleep -Seconds 15  # replication before SP creation
} else {
    Write-Host "  Found existing, AppId=$($app.AppId)"
}

$sp = Get-MgServicePrincipal -Filter "appId eq '$($app.AppId)'" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $sp) {
    $sp = New-MgServicePrincipal -AppId $app.AppId
    Write-Host "  Created Service Principal $($sp.Id)"
    Start-Sleep -Seconds 10
}

# ---- Federated identity credentials -----------------------------------------
# A FIC binds a GitHub workflow run (subject) to this app registration.
# Adding both the branch FIC (for push-to-main deploys) and the pull-request
# FIC (so future PR workflows can validate against Azure if needed).
$existingFics = Get-MgApplicationFederatedIdentityCredential -ApplicationId $app.Id
$desiredFics = @(
    @{
        Name = 'github-main-branch'
        Subject = "repo:${GitHubOrg}/${GitHubRepo}:ref:refs/heads/${MainBranch}"
        Description = "$GitHubOrg/$GitHubRepo @ refs/heads/$MainBranch"
    },
    @{
        Name = 'github-pull-request'
        Subject = "repo:${GitHubOrg}/${GitHubRepo}:pull_request"
        Description = "$GitHubOrg/$GitHubRepo pull_request"
    }
)
foreach ($f in $desiredFics) {
    if ($existingFics | Where-Object { $_.Name -eq $f.Name }) {
        Write-Host "  FIC '$($f.Name)' already exists"
        continue
    }
    New-MgApplicationFederatedIdentityCredential -ApplicationId $app.Id -BodyParameter @{
        name = $f.Name
        issuer = 'https://token.actions.githubusercontent.com'
        subject = $f.Subject
        audiences = @('api://AzureADTokenExchange')
        description = $f.Description
    } | Out-Null
    Write-Host "  Added FIC '$($f.Name)' for $($f.Subject)"
}

# ---- Role assignment --------------------------------------------------------
$scope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupName"
$existingRole = Get-AzRoleAssignment -ObjectId $sp.Id -Scope $scope -RoleDefinitionName 'Contributor' -ErrorAction SilentlyContinue
if (-not $existingRole) {
    New-AzRoleAssignment -ObjectId $sp.Id -RoleDefinitionName 'Contributor' -Scope $scope | Out-Null
    Write-Host "  Assigned Contributor on $ResourceGroupName"
} else {
    Write-Host "  Contributor already assigned on $ResourceGroupName"
}

# ---- Key Vault Secrets Officer (for rotate-easy-auth-secret workflow) -------
# The KV is RBAC-authorised; Contributor on the RG lets the SP manage KV
# resources but not read/write secret data. Secrets Officer gives it write
# access to secrets specifically — scoped to the single KV, not the whole RG.
$kvs = Get-AzKeyVault -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue
if ($kvs.Count -eq 1) {
    $kvScope = $kvs[0].ResourceId
    $existingKvRole = Get-AzRoleAssignment -ObjectId $sp.Id -Scope $kvScope -RoleDefinitionName 'Key Vault Secrets Officer' -ErrorAction SilentlyContinue
    if (-not $existingKvRole) {
        New-AzRoleAssignment -ObjectId $sp.Id -RoleDefinitionName 'Key Vault Secrets Officer' -Scope $kvScope | Out-Null
        Write-Host "  Assigned Key Vault Secrets Officer on $($kvs[0].VaultName)"
    } else {
        Write-Host "  Key Vault Secrets Officer already assigned on $($kvs[0].VaultName)"
    }
} else {
    Write-Host "  (Skipping KV role assignment — $($kvs.Count) vaults in RG, expected 1. Re-run after deploy.ps1 has created the KV.)"
}

# ---- Ownership of the Easy Auth app registration ----------------------------
# The CI SP needs to rotate passwords on this app reg via Graph. Ownership +
# Application.ReadWrite.OwnedBy (below) together let it do that without the
# broader Application.ReadWrite.All Graph role.
$easyAuthApp = Get-MgApplication -Filter "displayName eq '$EasyAuthAppRegName'" -ConsistencyLevel eventual -CountVariable _ | Select-Object -First 1
if ($easyAuthApp) {
    $currentOwners = Get-MgApplicationOwner -ApplicationId $easyAuthApp.Id
    if ($currentOwners | Where-Object { $_.Id -eq $sp.Id }) {
        Write-Host "  Already an owner of '$EasyAuthAppRegName' app registration"
    } else {
        New-MgApplicationOwnerByRef -ApplicationId $easyAuthApp.Id -BodyParameter @{
            '@odata.id' = "https://graph.microsoft.com/v1.0/directoryObjects/$($sp.Id)"
        }
        Write-Host "  Added CI SP as owner of '$EasyAuthAppRegName' app registration"
    }
} else {
    Write-Host "  (Skipping app-reg ownership — '$EasyAuthAppRegName' not found. Re-run after deploy.ps1 has created it.)"
}

# ---- Microsoft Graph app role: Application.ReadWrite.OwnedBy ----------------
# This is the narrow Graph role that, combined with owner-of-app-reg above,
# lets the CI SP rotate passwords on ONLY the app regs it owns. Granting the
# app role IS the admin consent — the user running this script needs
# Application.ReadWrite.All or Global Admin to issue it.
$graphSp = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'" | Select-Object -First 1
$ownedByRole = $graphSp.AppRoles | Where-Object { $_.Value -eq 'Application.ReadWrite.OwnedBy' } | Select-Object -First 1
$existingAssignment = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $sp.Id -ErrorAction SilentlyContinue |
    Where-Object { $_.AppRoleId -eq $ownedByRole.Id -and $_.ResourceId -eq $graphSp.Id }
if (-not $existingAssignment) {
    # Granting a Graph app role to an SP requires AppRoleAssignment.ReadWrite.All
    # in the Graph session AND Global Admin / Privileged Role Administrator in
    # the tenant. If the user doesn't have those, the call returns 403 — catch
    # it and tell them to grant the permission via the Portal instead, which
    # doesn't need those scopes on the caller's session.
    try {
        New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $sp.Id -ErrorAction Stop -BodyParameter @{
            principalId = $sp.Id
            resourceId  = $graphSp.Id
            appRoleId   = $ownedByRole.Id
        } | Out-Null
        Write-Host "  Granted Graph Application.ReadWrite.OwnedBy to CI SP"
    }
    catch {
        Write-Warning "  Couldn't grant Graph Application.ReadWrite.OwnedBy programmatically: $($_.Exception.Message)"
        Write-Warning "  Grant it via Portal instead: Entra -> App registrations -> $CiAppName -> API permissions ->"
        Write-Warning "    + Add permission -> Microsoft Graph -> Application permissions -> Application.ReadWrite.OwnedBy"
        Write-Warning "    -> Add, then 'Grant admin consent for <tenant>'."
        Write-Warning "  The rotation workflow will fail until this is granted."
    }
} else {
    Write-Host "  Graph Application.ReadWrite.OwnedBy already granted"
}

# ---- Discover the App Service name in the RG --------------------------------
$appService = Get-AzResource -ResourceGroupName $ResourceGroupName -ResourceType 'Microsoft.Web/sites' -ErrorAction SilentlyContinue | Select-Object -First 1
$appServiceName = if ($appService) { $appService.Name } else { '<run infra/deploy.ps1 first>' }

Write-Host ""
Write-Host "=========================================================="
Write-Host "GitHub repository variables (Settings -> Secrets and variables -> Actions -> Variables):"
Write-Host ""
Write-Host "  AZURE_CLIENT_ID       = $($app.AppId)"
Write-Host "  AZURE_TENANT_ID       = $TenantId"
Write-Host "  AZURE_SUBSCRIPTION_ID = $SubscriptionId"
Write-Host "  AZURE_RG              = $ResourceGroupName"
Write-Host "  AZURE_APP_NAME        = $appServiceName"
Write-Host "  AZURE_SLOT_NAME       = $SlotName"
Write-Host ""
Write-Host "These are *variables*, not secrets. OIDC means no client secret is stored anywhere."
Write-Host "=========================================================="
