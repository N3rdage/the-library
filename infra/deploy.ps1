# Requires: Az.Accounts, Az.Resources, Microsoft.Graph.Applications,
#           Microsoft.Graph.Users, SqlServer (v22+ for AAD token support).
# The script installs any missing modules to CurrentUser scope.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $TenantId,
    [Parameter(Mandatory)] [string] $SubscriptionId,
    [string] $Location = 'australiaeast',
    [string] $AppName = 'booktracker',
    [string] $EnterpriseAppName = 'Library-Patrons'
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
Write-Host "  Secret expires $($pwdCred.EndDateTime). TODO: schedule rotation."

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
$appHost = ([Uri]$appUrl).Host

Write-Host ""
Write-Host "Infrastructure deployed."
Write-Host "  App Service: $appUrl"
Write-Host "  SQL Server:  $sqlFqdn / $sqlDb"

# ---- Ensure the Easy Auth redirect URI is registered -------------------------
$expectedRedirect = "https://$appHost/.auth/login/aad/callback"
$currentUris = @($app.Web.RedirectUris)
if ($currentUris -notcontains $expectedRedirect) {
    $newUris = @($currentUris + $expectedRedirect | Where-Object { $_ } | Select-Object -Unique)
    Update-MgApplication -ApplicationId $app.Id -Web @{
        RedirectUris = $newUris
        ImplicitGrantSettings = @{
            EnableIdTokenIssuance  = $true
            EnableAccessTokenIssuance = $false
        }
    }
    Write-Host "  Added redirect URI: $expectedRedirect"
}

# ---- Grant the App Service managed identity access to the SQL DB -------------
# With AAD-only SQL, this is the only way the app can reach the database.
Write-Host "Granting App Service managed identity access to SQL..."

# The "Allow Azure services" firewall rule lets the App Service in but not this
# machine, so punch a temporary hole for the script's public IP.
$sqlServerResourceName = ($sqlFqdn -split '\.')[0]
$rgName = $deployment.Outputs.resourceGroupName.Value
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
    $sqlScript = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$appServiceName')
BEGIN
    CREATE USER [$appServiceName] FROM EXTERNAL PROVIDER;
    ALTER ROLE db_datareader ADD MEMBER [$appServiceName];
    ALTER ROLE db_datawriter ADD MEMBER [$appServiceName];
    ALTER ROLE db_ddladmin  ADD MEMBER [$appServiceName];
END
"@

    $token = (Get-AzAccessToken -ResourceUrl 'https://database.windows.net').Token
    Invoke-Sqlcmd -ServerInstance $sqlFqdn -Database $sqlDb -AccessToken $token -Query $sqlScript -Encrypt Mandatory
}
finally {
    Write-Host "  Removing temporary SQL firewall rule..."
    Remove-AzSqlServerFirewallRule `
        -ResourceGroupName $rgName `
        -ServerName $sqlServerResourceName `
        -FirewallRuleName $tempRuleName `
        -Force -ErrorAction SilentlyContinue | Out-Null
}

Write-Host ""
Write-Host "Done."
Write-Host "  App URL: $appUrl"
Write-Host ""
Write-Host "Next step — assign users/groups to the '$EnterpriseAppName' enterprise app:"
Write-Host "  https://entra.microsoft.com -> Identity -> Applications -> Enterprise applications -> $EnterpriseAppName -> Users and groups"
Write-Host "Only assigned principals will be able to sign in."
