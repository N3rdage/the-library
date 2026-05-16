# Dump the production BookTracker catalogue to a single JSON file for
# upload into the "BookTracker Analysis" Claude Project.
#
# Read-only operation against prod. Connects via AAD (Drew is SQL AAD
# admin on the prod server per deploy.ps1's `sqlAadAdminObjectId = $me.Id`
# wiring) — there are no SQL-auth credentials to handle.
#
# Prereqs:
#   - .NET 10 SDK on PATH (the script invokes `dotnet run` against the
#     BookTracker.Tools.SnapshotDump project).
#   - Az PowerShell modules (auto-installed if missing).
#   - Signed-in Az account with permission to (a) read the prod SQL
#     server resource, (b) temporarily add a firewall rule. The script
#     uses the same temp-firewall pattern as refresh-local-db.ps1.
#   - Drew himself: the prod SQL server's AAD admin (set at deploy
#     time). Anyone else needs an explicit `CREATE USER ... FROM
#     EXTERNAL PROVIDER` + role grant on the booktracker database.
#
# Typical run:
#   ./infra/dump-prod-to-json.ps1 -TenantId '<guid>' -SubscriptionId '<guid>'
#
# Output:
#   .\snapshots\booktracker-<yyyy-MM-dd-HHmm>.json (gitignored)
#
# Cost / impact:
#   - Read-only — no schema or data changes.
#   - Single short transaction; runs through EF Core's normal split-
#     query loaders. Catalogue is O(thousands of rows) so the dump
#     completes in seconds.
#   - Firewall rule is opened for the caller's public IP and removed
#     in the `finally` block, even if the dotnet step fails.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $TenantId,
    [Parameter(Mandatory)] [string] $SubscriptionId,

    [string] $AppName             = 'booktracker',
    [string] $ResourceGroupName   = 'rg-booktracker-prod',
    [string] $ProdDatabaseName    = 'booktracker',

    # Defaults to .\snapshots\booktracker-<stamp>.json under the repo root.
    [string] $OutputPath          = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ---- Resolve output path -----------------------------------------------------
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$snapshotDir = Join-Path $repoRoot 'snapshots'
if (-not (Test-Path $snapshotDir)) {
    New-Item -ItemType Directory -Path $snapshotDir -Force | Out-Null
}

if (-not $OutputPath) {
    $stamp = Get-Date -Format 'yyyy-MM-dd-HHmm'
    $OutputPath = Join-Path $snapshotDir "booktracker-$stamp.json"
}

# ---- Prereq: dotnet ---------------------------------------------------------
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw "dotnet is not on PATH. Install the .NET 10 SDK from https://dotnet.microsoft.com/."
}

# ---- Modules + sign-in -------------------------------------------------------
$required = @('Az.Accounts','Az.Resources','Az.Sql')
foreach ($m in $required) {
    if (-not (Get-Module -ListAvailable -Name $m)) {
        Write-Host "Installing $m..."
        Install-Module $m -Scope CurrentUser -Force -AllowClobber -Repository PSGallery
    }
    Import-Module $m -ErrorAction Stop | Out-Null
}

Write-Host "Connecting to Azure ($TenantId / $SubscriptionId)..."
Connect-AzAccount -Tenant $TenantId -Subscription $SubscriptionId | Out-Null
Set-AzContext -Tenant $TenantId -Subscription $SubscriptionId | Out-Null

# ---- Locate the prod SQL server ---------------------------------------------
# Naming convention from resources.bicep: "<AppName>-sql-<uniqueSuffix>".
# Same lookup as refresh-local-db.ps1.
$sqlServers = Get-AzSqlServer -ResourceGroupName $ResourceGroupName
$sqlServer = $sqlServers | Where-Object { $_.ServerName -like "$AppName-sql-*" } | Select-Object -First 1
if (-not $sqlServer) {
    throw "No SQL server matching '$AppName-sql-*' found in resource group '$ResourceGroupName'."
}
$sqlServerName = $sqlServer.ServerName
$sqlServerFqdn = "$sqlServerName.database.windows.net"
Write-Host "Prod SQL: $sqlServerFqdn / $ProdDatabaseName"

# ---- Open temp firewall for caller's IP -------------------------------------
$publicAccessWas = (Get-AzSqlServer -ResourceGroupName $ResourceGroupName -ServerName $sqlServerName).PublicNetworkAccess
if ($publicAccessWas -ne 'Enabled') {
    Write-Host "Temporarily enabling SQL public network access..."
    Set-AzSqlServer -ResourceGroupName $ResourceGroupName -ServerName $sqlServerName -PublicNetworkAccess 'Enabled' | Out-Null
}

$myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 10).Trim()
$tempRuleName = "dump-$([guid]::NewGuid().ToString('N').Substring(0,8))"
Write-Host "Adding temp firewall rule for $myIp ($tempRuleName)..."
New-AzSqlServerFirewallRule `
    -ResourceGroupName $ResourceGroupName `
    -ServerName $sqlServerName `
    -FirewallRuleName $tempRuleName `
    -StartIpAddress $myIp `
    -EndIpAddress $myIp | Out-Null
# Server-side propagation can lag the API response (~5-10s).
Start-Sleep -Seconds 10

try {
    # AAD-auth connection string. `Active Directory Default` picks up
    # the Az PowerShell session via Azure.Identity's DefaultAzureCredential
    # chain. Same auth mode the prod App Service uses (app-config.bicep).
    $connStr = "Server=tcp:$sqlServerFqdn,1433;Database=$ProdDatabaseName;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;"
    $env:ConnectionStrings__DefaultConnection = $connStr

    $cliProject = Join-Path $repoRoot 'BookTracker.Tools.SnapshotDump' 'BookTracker.Tools.SnapshotDump.csproj'
    Write-Host "Running snapshot dump..."
    Write-Host ""
    & dotnet run --project $cliProject --configuration Release -- --out $OutputPath --source prod
    if ($LASTEXITCODE -ne 0) {
        throw "Snapshot dump CLI failed (exit $LASTEXITCODE)."
    }
}
finally {
    # Always clean up — don't leave a stale firewall rule pointing at
    # the caller's IP, and don't leave the SQL server publicly reachable
    # if it wasn't before.
    Write-Host ""
    Write-Host "Removing temp firewall rule..."
    Remove-AzSqlServerFirewallRule `
        -ResourceGroupName $ResourceGroupName `
        -ServerName $sqlServerName `
        -FirewallRuleName $tempRuleName `
        -Force -ErrorAction SilentlyContinue | Out-Null

    if ($publicAccessWas -ne 'Enabled') {
        Write-Host "Restoring SQL public network access to '$publicAccessWas'..."
        Set-AzSqlServer -ResourceGroupName $ResourceGroupName -ServerName $sqlServerName -PublicNetworkAccess $publicAccessWas -ErrorAction SilentlyContinue | Out-Null
    }

    Remove-Item Env:\ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Done. Upload $OutputPath to the 'BookTracker Analysis' Claude Project."
