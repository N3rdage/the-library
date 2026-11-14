# One-time setup for the on-demand Bookshelf APK workflow (.github/workflows/bookshelf-apk.yml).
#
# Creates a SECOND, deliberately tiny Azure identity, separate from booktracker-ci:
#   - app registration + service principal `booktracker-apk-upload`
#   - one federated credential, for the GitHub Environment `bookshelf-apk` only
#   - one role: Storage Blob Data Contributor on the `bookshelf-builds` container only
#
# Why not reuse booktracker-ci: the APK workflow has to run on pull-request branches (a
# review build of a major Bookshelf change), and a branch's workflow files and build
# scripts are unreviewed code. booktracker-ci is Contributor on the production resource
# group; this identity can only read and write APK blobs. The GitHub Environment also
# requires Drew's approval before any run can use it or read its secrets.
#
# Safe to re-run. Prints the two GitHub Environment variables to configure.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $TenantId,
    [Parameter(Mandatory)] [string] $SubscriptionId,
    [Parameter(Mandatory)] [string] $GitHubOrg,
    [Parameter(Mandatory)] [string] $GitHubRepo,
    [string] $AppName = 'booktracker-apk-upload',
    [string] $EnvironmentName = 'bookshelf-apk',
    [string] $ResourceGroupName = 'rg-booktracker-prod',
    [string] $ContainerName = 'bookshelf-builds'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$required = @('Az.Accounts','Az.Resources','Az.Storage','Microsoft.Graph.Applications')
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

# ---- App registration + service principal -----------------------------------
Write-Host "Ensuring App Registration '$AppName'..."
$app = Get-MgApplication -Filter "displayName eq '$AppName'" -ConsistencyLevel eventual -CountVariable c | Select-Object -First 1
if (-not $app) {
    $app = New-MgApplication -DisplayName $AppName -SignInAudience 'AzureADMyOrg'
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

# ---- The one federated credential: the GitHub Environment --------------------
$subject = "repo:${GitHubOrg}/${GitHubRepo}:environment:${EnvironmentName}"
$existingFics = Get-MgApplicationFederatedIdentityCredential -ApplicationId $app.Id
if ($existingFics | Where-Object { $_.Subject -eq $subject }) {
    Write-Host "  FIC for $subject already exists"
} else {
    New-MgApplicationFederatedIdentityCredential -ApplicationId $app.Id -BodyParameter @{
        name = "github-environment-$EnvironmentName"
        issuer = 'https://token.actions.githubusercontent.com'
        subject = $subject
        audiences = @('api://AzureADTokenExchange')
        description = "$GitHubOrg/$GitHubRepo environment:$EnvironmentName (bookshelf-apk.yml)"
    } | Out-Null
    Write-Host "  Added FIC for $subject"
}
foreach ($f in ($existingFics | Where-Object { $_.Subject -ne $subject })) {
    Write-Warning "Unexpected federated credential on ${AppName}: '$($f.Name)' ($($f.Subject)). This identity should have exactly one; review and remove it."
}

# ---- The one role: blob data on the one container ----------------------------
$account = Get-AzStorageAccount -ResourceGroupName $ResourceGroupName | Where-Object { $_.StorageAccountName -like 'booktrackercovers*' } | Select-Object -First 1
if (-not $account) { throw "No storage account matching booktrackercovers* in $ResourceGroupName" }
$scope = "$($account.Id)/blobServices/default/containers/$ContainerName"
$existingRole = Get-AzRoleAssignment -ObjectId $sp.Id -Scope $scope -RoleDefinitionName 'Storage Blob Data Contributor' -ErrorAction SilentlyContinue
if (-not $existingRole) {
    New-AzRoleAssignment -ObjectId $sp.Id -RoleDefinitionName 'Storage Blob Data Contributor' -Scope $scope | Out-Null
    Write-Host "  Assigned Storage Blob Data Contributor on container '$ContainerName' only"
} else {
    Write-Host "  Storage Blob Data Contributor already assigned on container '$ContainerName'"
}
$other = Get-AzRoleAssignment -ObjectId $sp.Id | Where-Object { $_.Scope -ne $scope }
foreach ($r in $other) {
    Write-Warning "Unexpected role on ${AppName}: $($r.RoleDefinitionName) at $($r.Scope). This identity should hold one role on one container."
}

Write-Host ''
Write-Host "Set these as VARIABLES on the GitHub Environment '$EnvironmentName'"
Write-Host "(repo Settings > Environments > $EnvironmentName > Environment variables):"
Write-Host "  BOOKSHELF_APK_CLIENT_ID   = $($app.AppId)"
Write-Host "  BOOKSHELF_STORAGE_ACCOUNT = $($account.StorageAccountName)"
