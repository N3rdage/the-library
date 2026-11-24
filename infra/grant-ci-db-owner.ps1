# Makes the GitHub Actions identity db_owner on the production and staging
# databases, and nothing else. refresh-staging-db.yml needs that role to set
# the database users in the copy of production it puts on staging.
#
# deploy.ps1 makes the same grant when it is given -GitHubOidcAppName, but it
# also runs the whole infrastructure deployment. This script is the grant alone,
# for when that is all that is wanted. It takes no parameters and asks for
# nothing: it signs you in, finds the SQL server by its name, and finds the CI
# identity as the one external user that already holds db_ddladmin (the role
# deploy.ps1 gave it for migrations). Run it as the SQL server's AAD admin.
#
# Typical run:
#   ./infra/grant-ci-db-owner.ps1
#
# Re-runs are safe: an identity that is already db_owner is left as it is.

[CmdletBinding()]
param(
    # Only needed if your default sign-in lands in a different tenant from the
    # one that holds the SQL server.
    [string] $TenantId = '',
    [string] $ProdDatabaseName = 'booktracker',
    [string] $StagingDatabaseName = 'booktracker-staging'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

foreach ($m in @('Az.Accounts', 'Az.Resources', 'Az.Sql', 'SqlServer')) {
    if (-not (Get-Module -ListAvailable -Name $m)) {
        Write-Host "Installing $m..."
        Install-Module $m -Scope CurrentUser -Force -AllowClobber -Repository PSGallery
    }
    Import-Module $m -ErrorAction Stop
}

Write-Host 'Signing in to Azure...'
if ($TenantId) { Connect-AzAccount -Tenant $TenantId | Out-Null } else { Connect-AzAccount | Out-Null }

# ---- Find the SQL server -----------------------------------------------------
# Look through every subscription the sign-in can see for the one server whose
# name starts with booktracker-sql-.
$found = @()
foreach ($sub in (Get-AzSubscription -WarningAction SilentlyContinue)) {
    try {
        Set-AzContext -Subscription $sub.Id -Tenant $sub.TenantId -WarningAction SilentlyContinue | Out-Null
    } catch {
        continue
    }
    $servers = Get-AzResource -ResourceType 'Microsoft.Sql/servers' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'booktracker-sql-*' }
    foreach ($s in $servers) {
        $found += [pscustomobject]@{ Subscription = $sub.Id; Tenant = $sub.TenantId; ResourceGroup = $s.ResourceGroupName; Name = $s.Name }
    }
}
if ($found.Count -ne 1) {
    throw "Expected exactly one SQL server named booktracker-sql-*, found $($found.Count). If it is in another tenant, run again with -TenantId."
}
$server = $found[0]
Set-AzContext -Subscription $server.Subscription -Tenant $server.Tenant | Out-Null
$rgName = $server.ResourceGroup
$serverName = $server.Name
$sqlFqdn = "$serverName.database.windows.net"
Write-Host 'Found the SQL server.'

# ---- Open the firewall for this machine, as deploy.ps1 does -------------------
$sqlServer = Get-AzSqlServer -ResourceGroupName $rgName -ServerName $serverName
$publicAccessWas = $sqlServer.PublicNetworkAccess
$myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 10).Trim()
if ($myIp -notmatch '^(\d{1,3}\.){3}\d{1,3}$') {
    throw 'The address lookup did not return a single IPv4 address.'
}
$tempRuleName = "grant-script-$([guid]::NewGuid().ToString('N').Substring(0,8))"

$results = @()
try {
    if ($publicAccessWas -ne 'Enabled') {
        Write-Host 'Temporarily enabling SQL public network access...'
        Set-AzSqlServer -ResourceGroupName $rgName -ServerName $serverName -PublicNetworkAccess 'Enabled' | Out-Null
    }
    Write-Host 'Adding a temporary firewall rule for this machine...'
    New-AzSqlServerFirewallRule `
        -ResourceGroupName $rgName `
        -ServerName $serverName `
        -FirewallRuleName $tempRuleName `
        -StartIpAddress $myIp `
        -EndIpAddress $myIp | Out-Null
    # Server-side propagation can lag the API response by a few seconds.
    Start-Sleep -Seconds 10

    $token = (Get-AzAccessToken -ResourceUrl 'https://database.windows.net').Token
    if ($token -is [System.Security.SecureString]) {
        $token = [System.Net.NetworkCredential]::new('', $token).Password
    }

    # The CI identity is the external user or group that holds db_ddladmin.
    # The app's own identities never hold it: deploy.ps1 removes it from them.
    $findSql = @"
SELECT m.name
FROM sys.database_role_members rm
JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
WHERE r.name = 'db_ddladmin' AND m.type IN ('E', 'X');
"@

    foreach ($db in @($ProdDatabaseName, $StagingDatabaseName)) {
        $members = @(Invoke-Sqlcmd -ServerInstance $sqlFqdn -Database $db -AccessToken $token -Query $findSql -Encrypt Mandatory)
        if ($members.Count -ne 1) {
            throw "Expected exactly one external identity with db_ddladmin on '$db', found $($members.Count). Run deploy.ps1 with -GitHubOidcAppName instead, which names the identity."
        }
        $name = [string] $members[0].name
        $bracketed = $name.Replace(']', ']]')
        $literal = $name.Replace("'", "''")
        $grantSql = @"
IF ISNULL(IS_ROLEMEMBER('db_owner', '$literal'), 0) = 0
    ALTER ROLE db_owner ADD MEMBER [$bracketed];
SELECT ISNULL(IS_ROLEMEMBER('db_owner', '$literal'), 0) AS is_owner;
"@
        $check = Invoke-Sqlcmd -ServerInstance $sqlFqdn -Database $db -AccessToken $token -Query $grantSql -Encrypt Mandatory
        if ([int] $check.is_owner -ne 1) {
            throw "The grant did not take effect on '$db'."
        }
        $results += "  $db : the CI identity is db_owner"
    }
}
finally {
    Write-Host 'Removing the temporary firewall rule...'
    Remove-AzSqlServerFirewallRule `
        -ResourceGroupName $rgName `
        -ServerName $serverName `
        -FirewallRuleName $tempRuleName `
        -Force -ErrorAction SilentlyContinue | Out-Null
    if ($publicAccessWas -ne 'Enabled') {
        Write-Host "Restoring SQL public network access to '$publicAccessWas'..."
        Set-AzSqlServer -ResourceGroupName $rgName -ServerName $serverName -PublicNetworkAccess $publicAccessWas -ErrorAction SilentlyContinue | Out-Null
    }
}

Write-Host ''
Write-Host 'Done.'
$results | ForEach-Object { Write-Host $_ }
