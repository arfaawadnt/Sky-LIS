<#
.SYNOPSIS
    Restores a Sky LIS pg_dump archive into a named database (restore drill, or production recovery).

.DESCRIPTION
    Creates the target database (owner lis_owner, connect/temporary granted to lis_app) with the superuser, then
    restores the archive as lis_owner with pg_restore --no-owner --role=lis_owner, so every table ends up owned by
    lis_owner exactly as a migration would leave it (the start-up guard refuses a runtime role that owns tables).
    Afterwards it reports the number of restored tables and applied migrations (ops.__ef_migrations_history).

    Target names must start with "lis" (the only names the application accepts). The production database "lis" is
    refused unless -AllowProductionTarget is given; a production recovery also needs the NTLIS service stopped
    (the runbook has the full sequence). The .sha256 sidecar written by backup-database.ps1 is verified when present.

    Connection details come from the same git-ignored env file as backup-database.ps1 (KEY=VALUE lines):
        LIS_PG_SUPERUSER_PASSWORD  password of the superuser that creates the database (required)
        LIS_PG_SUPERUSER           default postgres
        LIS_OWNER_PASSWORD         password of lis_owner (required)
        LIS_PG_HOST                default 127.0.0.1
        LIS_PG_PORT                default 5432
        LIS_PG_BIN                 default C:\Program Files\PostgreSQL\17\bin
    Passwords reach psql/pg_restore through PGPASSWORD for the duration of each call only.

.PARAMETER DumpFile
    The archive to restore (<database>-<timestamp>.dump).
.PARAMETER TargetDatabase
    Default lis_drill.
.PARAMETER Replace
    Drop the target database first when it exists (DROP DATABASE ... WITH (FORCE)).
.PARAMETER AllowProductionTarget
    Required when the target is the production database "lis".
.PARAMETER Jobs
    pg_restore parallel jobs (default 2).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\restore-database.ps1 -DumpFile E:\lis-backups\lis-20261010-020000.dump -WhatIf
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\restore-database.ps1 -DumpFile E:\lis-backups\lis-20261010-020000.dump -TargetDatabase lis_drill -Replace
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $DumpFile,

    [ValidatePattern('^lis[a-z0-9_]*$')]
    [string] $TargetDatabase = 'lis_drill',

    [string] $EnvFile = '',

    [switch] $Replace,
    [switch] $AllowProductionTarget,

    [ValidateRange(1, 16)]
    [int] $Jobs = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string] $Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

function Read-EnvFile([string] $Path) {
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $index = $trimmed.IndexOf('=')
        if ($index -lt 1) { continue }
        $key = $trimmed.Substring(0, $index).Trim()
        $value = $trimmed.Substring($index + 1).Trim()
        if ($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))) { $value = $value.Substring(1, $value.Length - 2) }
        $values[$key] = $value
    }
    return $values
}

function Get-Setting([hashtable] $Values, [string] $Key, [string] $Default) {
    if ($Values.ContainsKey($Key) -and $Values[$Key] -ne '') { return $Values[$Key] }
    return $Default
}

function Invoke-Psql([string] $User, [string] $Password, [string] $DatabaseName, [string[]] $Commands) {
    $arguments = @("--host=$pgHost", "--port=$pgPort", "--username=$User", "--dbname=$DatabaseName", '--no-password', '--tuples-only', '--no-align', '--set=ON_ERROR_STOP=1')
    foreach ($command in $Commands) { $arguments += @('--command', $command) }
    $env:PGPASSWORD = $Password
    try {
        $output = & $psql @arguments
        if ($LASTEXITCODE -ne 0) { throw "psql exited with code $LASTEXITCODE while running: $($Commands -join ' | ')" }
        return ($output | Out-String).Trim()
    } finally {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }
}

# --- settings -----------------------------------------------------------------------------------------------------
if ($EnvFile -eq '') {
    $candidates = @('E:\lis-data\postgres.env', (Join-Path (Split-Path -Parent $PSScriptRoot) 'postgres\.env'))
    $EnvFile = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $EnvFile) { throw "No env file found ($($candidates -join ', ')). Pass -EnvFile." }
}
$settings = Read-EnvFile $EnvFile
$pgHost = Get-Setting $settings 'LIS_PG_HOST' '127.0.0.1'
$pgPort = [int] (Get-Setting $settings 'LIS_PG_PORT' '5432')
$pgBin = Get-Setting $settings 'LIS_PG_BIN' 'C:\Program Files\PostgreSQL\17\bin'
$superuser = Get-Setting $settings 'LIS_PG_SUPERUSER' 'postgres'
$superuserPassword = Get-Setting $settings 'LIS_PG_SUPERUSER_PASSWORD' ''
$ownerPassword = Get-Setting $settings 'LIS_OWNER_PASSWORD' ''
if ($superuserPassword -eq '') { throw "LIS_PG_SUPERUSER_PASSWORD is not set in $EnvFile (needed to create the target database)." }
if ($ownerPassword -eq '') { throw "LIS_OWNER_PASSWORD is not set in $EnvFile." }
$psql = Join-Path $pgBin 'psql.exe'
$pgRestore = Join-Path $pgBin 'pg_restore.exe'
foreach ($tool in @($psql, $pgRestore)) { if (-not (Test-Path -LiteralPath $tool)) { throw "$tool not found. Set LIS_PG_BIN in $EnvFile." } }

$DumpFile = (Resolve-Path -LiteralPath $DumpFile).Path
if ($TargetDatabase -eq 'lis' -and -not $AllowProductionTarget) { throw "Target 'lis' is the production database. Stop NTLIS first and re-run with -AllowProductionTarget (runbook: 'Restore into production')." }

Write-Host ''
Write-Host 'Sky LIS database restore' -ForegroundColor Green
Write-Host "  archive    : $DumpFile"
Write-Host "  target     : ${pgHost}:$pgPort/$TargetDatabase (owner lis_owner; created by $superuser)"
Write-Host "  replace    : $Replace"
Write-Host ''

# --- verify the archive -------------------------------------------------------------------------------------------
if (Test-Path -LiteralPath "$DumpFile.sha256") {
    $expected = ((Get-Content -LiteralPath "$DumpFile.sha256" -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $DumpFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { throw "SHA-256 mismatch: the sidecar says $expected, the file hashes to $actual." }
    Write-Host "  sha256 verified ($($actual.Substring(0, 12))...)"
} else {
    Write-Warning 'No .sha256 sidecar next to the archive; integrity not verified.'
}
$entries = & $pgRestore --list $DumpFile
if ($LASTEXITCODE -ne 0) { throw 'pg_restore --list failed: the archive is unreadable.' }
$archiveTables = @($entries | Where-Object { $_ -match '\sTABLE\s' }).Count
Write-Host "  archive holds $archiveTables table(s)"

# --- create the target ----------------------------------------------------------------------------------------------
$exists = (Invoke-Psql $superuser $superuserPassword 'postgres' @("SELECT 1 FROM pg_database WHERE datname = '$TargetDatabase'")) -eq '1'
if ($exists) {
    if (-not $Replace) { throw "Database $TargetDatabase already exists. Use -Replace to drop and recreate it." }
    if ($PSCmdlet.ShouldProcess($TargetDatabase, 'DROP DATABASE WITH (FORCE)')) {
        [void] (Invoke-Psql $superuser $superuserPassword 'postgres' @("DROP DATABASE $TargetDatabase WITH (FORCE)"))
    }
}
if ($PSCmdlet.ShouldProcess($TargetDatabase, 'CREATE DATABASE OWNER lis_owner; GRANT CONNECT, TEMPORARY TO lis_app')) {
    [void] (Invoke-Psql $superuser $superuserPassword 'postgres' @(
        "CREATE DATABASE $TargetDatabase OWNER lis_owner ENCODING 'UTF8' TEMPLATE template0",
        "REVOKE ALL ON DATABASE $TargetDatabase FROM PUBLIC",
        "GRANT CONNECT, TEMPORARY ON DATABASE $TargetDatabase TO lis_app"))
}

# --- restore --------------------------------------------------------------------------------------------------------
if ($PSCmdlet.ShouldProcess($TargetDatabase, "pg_restore $DumpFile as lis_owner")) {
    Write-Step "pg_restore --no-owner --role=lis_owner --jobs=$Jobs"
    $env:PGPASSWORD = $ownerPassword
    try {
        & $pgRestore --host=$pgHost --port=$pgPort --username=lis_owner --dbname=$TargetDatabase --no-password --no-owner --role=lis_owner --exit-on-error --jobs=$Jobs $DumpFile
        if ($LASTEXITCODE -ne 0) { throw "pg_restore exited with code $LASTEXITCODE." }
    } finally {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }

    $tables = Invoke-Psql 'lis_owner' $ownerPassword $TargetDatabase @("SELECT count(*) FROM pg_tables WHERE schemaname IN ('identity','app','audit','saas','read','ops')")
    $migrations = Invoke-Psql 'lis_owner' $ownerPassword $TargetDatabase @('SELECT count(*) FROM ops.__ef_migrations_history')
    $ownedByApp = Invoke-Psql 'lis_owner' $ownerPassword $TargetDatabase @("SELECT count(*) FROM pg_tables WHERE tableowner = 'lis_app'")
    if ([int] $ownedByApp -gt 0) { throw "$ownedByApp table(s) are owned by lis_app after the restore; the start-up guard would refuse this database." }
    Write-Host "  $TargetDatabase restored: $tables application table(s), $migrations migration(s) applied" -ForegroundColor Green
    Write-Host "  drill check: Host=$pgHost;Port=$pgPort;Database=$TargetDatabase;Username=lis_app as Lis:Database:ConnectionString of a test service, then /health/ready"
}
