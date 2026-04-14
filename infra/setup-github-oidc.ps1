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
    [string] $SlotName = 'staging'
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
Connect-MgGraph -TenantId $TenantId -Scopes 'Application.ReadWrite.All','Directory.Read.All' -NoWelcome

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
