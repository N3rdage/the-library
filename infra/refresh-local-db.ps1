# Refresh the local Docker SQL Server database with a copy of production.
#
# Uses SqlPackage.exe (BACPAC export/import). Preserves schema, data, and
# identity values. Prod → local only — there is no reverse direction; data
# flows into prod through normal app usage, never via this script.
#
# Prereqs:
#   - SqlPackage on PATH. Install via the .NET global tool:
#         dotnet tool install -g microsoft.sqlpackage
#     (Fallback alternatives: Azure Data Studio, or the standalone installer
#     at https://aka.ms/sqlpackage-windows.)
#   - Docker Desktop running, with `docker compose up -d` already applied so
#     the `booktracker-db` container is healthy.
#   - Signed-in Az account with permission to temporarily open the SQL
#     firewall for the current machine's IP.
#
# Typical run:
#   ./infra/refresh-local-db.ps1 -TenantId '<guid>' -SubscriptionId '<guid>'
#
# Re-runs:
#   Same command. The BACPAC lands in `./artifacts/` (gitignored). The local
#   DB is dropped and re-imported each run — say yes to the prompt, or pass
#   -Force to skip it.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $TenantId,
    [Parameter(Mandatory)] [string] $SubscriptionId,

    [string] $AppName            = 'booktracker',
    [string] $ResourceGroupName  = 'rg-booktracker-prod',
    [string] $ProdDatabaseName   = 'booktracker',

    [string] $LocalServer        = 'localhost,1433',
    [string] $LocalDatabaseName  = 'BookTracker',
    # Falls back to $env:MSSQL_SA_PASSWORD, then to docker-compose.yml's
    # published default.
    [string] $LocalSaPassword    = '',

    [string] $OutputDir          = (Join-Path $PSScriptRoot '..' 'artifacts'),

    # Skip the destructive-op prompt. Handy in reruns once you trust it.
    [switch] $Force,
    # Reuse the most recent BACPAC in $OutputDir instead of hitting prod.
    [switch] $SkipExport,
    # Download the BACPAC without touching the local DB.
    [switch] $SkipImport
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ---- Resolve local SA password -----------------------------------------------
if (-not $LocalSaPassword) {
    if ($env:MSSQL_SA_PASSWORD) {
        $LocalSaPassword = $env:MSSQL_SA_PASSWORD
    } else {
        $LocalSaPassword = 'BookTracker!Dev1'  # docker-compose.yml default
    }
}

# ---- Prereq checks -----------------------------------------------------------
$sqlPackage = Get-Command SqlPackage -ErrorAction SilentlyContinue
if (-not $sqlPackage) {
    throw @"
SqlPackage is not on PATH. Install it via:
    dotnet tool install -g microsoft.sqlpackage

(Alternatives: Azure Data Studio's 'SQL Database Projects' extension, or
the standalone installer at https://aka.ms/sqlpackage-windows.)

After install, open a fresh shell so the updated PATH is picked up, then
re-run this script.
"@
}

$containerUp = docker ps --filter 'name=booktracker-db' --filter 'status=running' --format '{{.Names}}' 2>$null
if (-not $containerUp) {
    throw @"
Local SQL Server container 'booktracker-db' is not running. From the repo root:
  docker compose up -d
Then retry this script.
"@
}

# ---- Modules + sign-in -------------------------------------------------------
$required = @('Az.Accounts','Az.Resources','Az.Sql','SqlServer')
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

# ---- Locate the prod SQL server ----------------------------------------------
# Naming from resources.bicep: "<AppName>-sql-<uniqueSuffix>". The suffix is
# deterministic per-RG but unknown to this script, so we look up by pattern.
$sqlServers = Get-AzSqlServer -ResourceGroupName $ResourceGroupName
$sqlServer = $sqlServers | Where-Object { $_.ServerName -like "$AppName-sql-*" } | Select-Object -First 1
if (-not $sqlServer) {
    throw "No SQL server matching '$AppName-sql-*' found in resource group '$ResourceGroupName'."
}
$sqlServerName = $sqlServer.ServerName
$sqlServerFqdn = "$sqlServerName.database.windows.net"
Write-Host "Prod SQL: $sqlServerFqdn / $ProdDatabaseName"

# ---- Export (unless skipped) -------------------------------------------------
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$bacpacPath = Join-Path $OutputDir "$ProdDatabaseName-$stamp.bacpac"

if ($SkipExport) {
    $existing = Get-ChildItem -Path $OutputDir -Filter '*.bacpac' -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1
    if (-not $existing) {
        throw "-SkipExport was specified but no .bacpac file found in $OutputDir."
    }
    $bacpacPath = $existing.FullName
    Write-Host "Reusing existing BACPAC: $bacpacPath"
}
else {
    # Temporarily enable public access + open firewall for the caller's IP.
    # Same pattern used by deploy.ps1's SQL AAD grant step.
    $publicAccessWas = (Get-AzSqlServer -ResourceGroupName $ResourceGroupName -ServerName $sqlServerName).PublicNetworkAccess
    if ($publicAccessWas -ne 'Enabled') {
        Write-Host "Temporarily enabling SQL public network access..."
        Set-AzSqlServer -ResourceGroupName $ResourceGroupName -ServerName $sqlServerName -PublicNetworkAccess 'Enabled' | Out-Null
    }

    $myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 10).Trim()
    $tempRuleName = "refresh-$([guid]::NewGuid().ToString('N').Substring(0,8))"
    Write-Host "Adding temp firewall rule for $myIp ($tempRuleName)..."
    New-AzSqlServerFirewallRule `
        -ResourceGroupName $ResourceGroupName `
        -ServerName $sqlServerName `
        -FirewallRuleName $tempRuleName `
        -StartIpAddress $myIp `
        -EndIpAddress $myIp | Out-Null
    # Server-side propagation can lag the API response.
    Start-Sleep -Seconds 10

    try {
        Write-Host "Exporting $ProdDatabaseName -> $bacpacPath (this can take a few minutes)..."
        & SqlPackage `
            /a:Export `
            /SourceServerName:$sqlServerFqdn `
            /SourceDatabaseName:$ProdDatabaseName `
            /TargetFile:$bacpacPath `
            /ua:true `
            /p:VerifyFullTextDocumentTypesSupported=false
        if ($LASTEXITCODE -ne 0) {
            throw "SqlPackage export failed (exit $LASTEXITCODE)."
        }
    }
    finally {
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
    }

    Write-Host "Export complete. File size: $((Get-Item $bacpacPath).Length / 1MB | ForEach-Object { '{0:N2}' -f $_ }) MB"
}

# ---- Import (unless skipped) -------------------------------------------------
if ($SkipImport) {
    Write-Host ""
    Write-Host "-SkipImport was specified. BACPAC at: $bacpacPath"
    return
}

if (-not $Force) {
    Write-Host ""
    Write-Host "About to DROP local database '$LocalDatabaseName' on '$LocalServer'"
    Write-Host "and import from: $bacpacPath"
    $resp = Read-Host "Continue? (y/N)"
    if ($resp -notmatch '^(y|yes)$') {
        Write-Host "Aborted. BACPAC kept at: $bacpacPath"
        return
    }
}

Write-Host "Dropping local database '$LocalDatabaseName' if present..."
# SINGLE_USER + ROLLBACK IMMEDIATE to evict any open sessions (the dev
# server holding a connection is the usual culprit).
$dropSql = @"
IF DB_ID('$LocalDatabaseName') IS NOT NULL
BEGIN
    ALTER DATABASE [$LocalDatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$LocalDatabaseName];
END
"@
Invoke-Sqlcmd `
    -ServerInstance $LocalServer `
    -Username 'sa' `
    -Password $LocalSaPassword `
    -Query $dropSql `
    -TrustServerCertificate `
    -Database 'master'

Write-Host "Importing BACPAC into $LocalServer / $LocalDatabaseName..."
& SqlPackage `
    /a:Import `
    /SourceFile:$bacpacPath `
    /TargetServerName:$LocalServer `
    /TargetDatabaseName:$LocalDatabaseName `
    /TargetUser:sa `
    /TargetPassword:$LocalSaPassword `
    /TargetTrustServerCertificate:true `
    /TargetEncryptConnection:false
if ($LASTEXITCODE -ne 0) {
    throw "SqlPackage import failed (exit $LASTEXITCODE)."
}

Write-Host ""
Write-Host "Done. Local database '$LocalDatabaseName' now mirrors prod as of $stamp."
Write-Host ""
Write-Host "If this branch has EF migrations not yet in prod, apply them now:"
Write-Host "  dotnet ef database update --project .\BookTracker.Data --startup-project .\BookTracker.Web"
