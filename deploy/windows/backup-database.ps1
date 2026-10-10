<#
.SYNOPSIS
    Backs up the Sky LIS database with pg_dump (custom format) into E:\lis-backups and applies the retention.

.DESCRIPTION
    Produces <BackupRoot>\<database>-<yyyyMMdd-HHmmss>.dump (pg_dump -Fc, compressed) plus a .sha256 sidecar, verifies
    the dump with pg_restore --list, then deletes dumps older than -RetentionDays. Runs as lis_owner (the only role
    that reads every table, including the audit schema and the row-level-security-fenced tenant tables).

    Connection details come from a git-ignored env file (KEY=VALUE lines, # comments), never from parameters:
        LIS_OWNER_PASSWORD         password of lis_owner (required)
        LIS_PG_HOST                default 127.0.0.1
        LIS_PG_PORT                default 5432 (the development instance of E:\LIS uses 5433)
        LIS_PG_DATABASE            default lis (must start with "lis")
        LIS_PG_BIN                 default C:\Program Files\PostgreSQL\17\bin
    Default env file: E:\lis-data\postgres.env on a server, else deploy\postgres\.env of the repository the script
    lives in. The password reaches pg_dump through the process environment (PGPASSWORD) and is cleared afterwards;
    nothing secret is printed or written.

    Schedule it (Task Scheduler, daily) under an administrator or a dedicated backup account that can read the env
    file and write <BackupRoot>; see the runbook. -WhatIf prints the plan and writes nothing.

.PARAMETER EnvFile
    The env file described above.
.PARAMETER BackupRoot
    Default E:\lis-backups.
.PARAMETER RetentionDays
    Dumps older than this are deleted (default 30; 0 keeps everything).
.PARAMETER Database
    Overrides LIS_PG_DATABASE (must start with "lis", e.g. lis_test).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\backup-database.ps1 -WhatIf
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\backup-database.ps1 -EnvFile E:\lis-data\postgres.env -RetentionDays 30
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $EnvFile = '',

    [ValidatePattern('^[A-Za-z]:\\[^\s"<>|*?]+$')]
    [string] $BackupRoot = 'E:\lis-backups',

    [ValidateRange(0, 3650)]
    [int] $RetentionDays = 30,

    [ValidatePattern('^(lis[a-z0-9_]*)?$')]
    [string] $Database = ''
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
if ($Database -eq '') { $Database = Get-Setting $settings 'LIS_PG_DATABASE' 'lis' }
if ($Database -notmatch '^lis[a-z0-9_]*$') { throw "Database '$Database' is not an LIS database (the name must start with 'lis')." }
$ownerPassword = Get-Setting $settings 'LIS_OWNER_PASSWORD' ''
if ($ownerPassword -eq '') { throw "LIS_OWNER_PASSWORD is not set in $EnvFile." }
$pgDump = Join-Path $pgBin 'pg_dump.exe'
$pgRestore = Join-Path $pgBin 'pg_restore.exe'
foreach ($tool in @($pgDump, $pgRestore)) { if (-not (Test-Path -LiteralPath $tool)) { throw "$tool not found. Set LIS_PG_BIN in $EnvFile." } }

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$dumpFile = Join-Path $BackupRoot "$Database-$stamp.dump"

Write-Host ''
Write-Host "Sky LIS database backup" -ForegroundColor Green
Write-Host "  source     : ${pgHost}:$pgPort/$Database as lis_owner (env file $EnvFile)"
Write-Host "  target     : $dumpFile"
Write-Host "  retention  : $(if ($RetentionDays -gt 0) { "$RetentionDays days" } else { 'keep everything' })"
Write-Host ''

if (-not (Test-Path -LiteralPath $BackupRoot)) {
    if ($PSCmdlet.ShouldProcess($BackupRoot, 'Create backup folder')) { New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null }
}

# --- dump ---------------------------------------------------------------------------------------------------------
if ($PSCmdlet.ShouldProcess($dumpFile, "pg_dump -Fc $Database")) {
    $env:PGPASSWORD = $ownerPassword
    try {
        Write-Step "pg_dump --format=custom --compress=6 $Database"
        & $pgDump --host=$pgHost --port=$pgPort --username=lis_owner --dbname=$Database --format=custom --compress=6 --no-password --file=$dumpFile
        if ($LASTEXITCODE -ne 0) { throw "pg_dump exited with code $LASTEXITCODE." }

        Write-Step 'Verifying the archive (pg_restore --list)'
        $entries = & $pgRestore --list $dumpFile
        if ($LASTEXITCODE -ne 0) { throw "pg_restore --list exited with code $LASTEXITCODE; the dump is unreadable." }
        $tableCount = @($entries | Where-Object { $_ -match '\sTABLE\s' }).Count
        if ($tableCount -lt 1) { throw 'The dump contains no table.' }
    } finally {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }

    $hash = (Get-FileHash -LiteralPath $dumpFile -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText("$dumpFile.sha256", "$hash *$(Split-Path -Leaf $dumpFile)`n", (New-Object System.Text.UTF8Encoding($false)))
    $size = (Get-Item -LiteralPath $dumpFile).Length
    Write-Host ("  {0}: {1:N1} MB, {2} tables, sha256 {3}" -f (Split-Path -Leaf $dumpFile), ($size / 1MB), $tableCount, $hash.Substring(0, 12)) -ForegroundColor Green
}

# --- retention ------------------------------------------------------------------------------------------------------
if ($RetentionDays -gt 0 -and (Test-Path -LiteralPath $BackupRoot)) {
    $cutoff = (Get-Date).AddDays(-$RetentionDays)
    $expired = @(Get-ChildItem -LiteralPath $BackupRoot -File | Where-Object { $_.Name -match "^$Database-\d{8}-\d{6}\.dump(\.sha256)?$" -and $_.LastWriteTime -lt $cutoff })
    foreach ($file in $expired) {
        if ($PSCmdlet.ShouldProcess($file.FullName, "Delete (older than $RetentionDays days)")) { Remove-Item -LiteralPath $file.FullName -Force }
    }
    Write-Host "  retention: $($expired.Count) file(s) older than $RetentionDays days removed"
}

$kept = @(Get-ChildItem -LiteralPath $BackupRoot -Filter "$Database-*.dump" -File -ErrorAction SilentlyContinue)
Write-Host "  $($kept.Count) dump(s) of $Database in $BackupRoot"
