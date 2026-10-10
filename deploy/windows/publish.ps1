<#
.SYNOPSIS
    Builds the Sky LIS Release package: API and DevTools (framework-dependent win-x64) and the SPA into
    publish\<version>\{api,tools,ui}.

.DESCRIPTION
    Runs on the build box (needs the .NET 9 SDK, Node 24 and npm 11, git). Output: <OutputRoot>\<version>\api,
    <OutputRoot>\<version>\tools (Platform.DevTools.exe for seed-platform-admin on the server),
    <OutputRoot>\<version>\ui and <OutputRoot>\<version>\manifest.json (version, git commit, dates, tool versions,
    file counts). The version comes from the VERSION file at the repository root. The package never contains a
    secret: appsettings.Development.json is removed from the API output and every JSON file in it is scanned for
    password-bearing values. Nothing outside <OutputRoot> (default E:\LIS\publish, git-ignored) is written.

.PARAMETER RepositoryRoot
    The repository (default: the parent of deploy\windows).
.PARAMETER OutputRoot
    Where the versioned package folders are created (default: <RepositoryRoot>\publish).
.PARAMETER BuildSlot
    LisBuildSlot used for the API compilation (default: publish), so the build never collides with a running
    development API that locks the default bin\Release.
.PARAMETER SkipApi / SkipUi
    Build only one half (the manifest records what was built).
.PARAMETER SkipNpmCi
    Reuse frontend\node_modules instead of running npm ci.
.PARAMETER Clean
    Remove an existing <OutputRoot>\<version> first; without it the script refuses to overwrite one.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\publish.ps1
.EXAMPLE
    powershell -NoProfile -File deploy\windows\publish.ps1 -Clean -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $RepositoryRoot = '',

    [string] $OutputRoot = '',

    [ValidatePattern('^[a-z][a-z0-9-]{0,31}$')]
    [string] $BuildSlot = 'publish',

    [switch] $SkipApi,
    [switch] $SkipUi,
    [switch] $SkipTools,
    [switch] $SkipNpmCi,
    [switch] $Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string] $Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

function Invoke-Native {
    param([string] $Description, [string] $WorkingDirectory, [string] $Command, [string[]] $Arguments)
    Write-Step "$Description`n    $Command $($Arguments -join ' ')  (in $WorkingDirectory)"
    if (-not $PSCmdlet.ShouldProcess($WorkingDirectory, "$Command $($Arguments -join ' ')")) { return }
    Push-Location $WorkingDirectory
    try {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Command exited with code $LASTEXITCODE ($Description)." }
    } finally {
        Pop-Location
    }
}

function Get-NativeOutput([string] $Command, [string[]] $Arguments, [string] $WorkingDirectory) {
    Push-Location $WorkingDirectory
    try {
        $output = & $Command @Arguments
        if ($LASTEXITCODE -ne 0) { return $null }
        return ($output | Out-String).Trim()
    } finally {
        Pop-Location
    }
}

function Test-FileLocked([string] $Path) {
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $stream.Dispose()
        return $false
    } catch [System.IO.IOException] {
        return $true
    }
}

function Get-FolderSize([string] $Path) {
    $files = @(Get-ChildItem -LiteralPath $Path -Recurse -File)
    $bytes = 0
    foreach ($file in $files) { $bytes += $file.Length }
    return [pscustomobject]@{ Files = $files.Count; Bytes = $bytes }
}

# --- inputs -----------------------------------------------------------------------------------------------------
# ($PSScriptRoot is empty inside param() defaults under Windows PowerShell 5.1, so the defaults are resolved here.)
if ($RepositoryRoot -eq '') { $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
foreach ($required in @('VERSION', 'Platform.sln', 'src\Platform.WebApi\Platform.WebApi.csproj', 'frontend\package.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $required))) { throw "$RepositoryRoot is not the LIS repository: $required is missing." }
}
if ($OutputRoot -eq '') { $OutputRoot = Join-Path $RepositoryRoot 'publish' }

$version = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw "VERSION holds '$version', expected MAJOR.MINOR.PATCH." }

$packageRoot = Join-Path $OutputRoot $version
$apiOut = Join-Path $packageRoot 'api'
$uiOut = Join-Path $packageRoot 'ui'
$frontend = Join-Path $RepositoryRoot 'frontend'
$webApiProject = Join-Path $RepositoryRoot 'src\Platform.WebApi\Platform.WebApi.csproj'
$devToolsProject = Join-Path $RepositoryRoot 'tools\Platform.DevTools\Platform.DevTools.csproj'
$toolsOut = Join-Path $packageRoot 'tools'

$commit = Get-NativeOutput 'git' @('rev-parse', 'HEAD') $RepositoryRoot
$commitDate = Get-NativeOutput 'git' @('log', '-1', '--format=%cI') $RepositoryRoot
$dirty = $false
$status = Get-NativeOutput 'git' @('status', '--porcelain', '--untracked-files=no') $RepositoryRoot
if ($status) { $dirty = $true }
$dotnetSdk = Get-NativeOutput 'dotnet' @('--version') $RepositoryRoot
$nodeVersion = Get-NativeOutput 'node' @('-v') $RepositoryRoot
$npmVersion = Get-NativeOutput 'npm' @('-v') $RepositoryRoot

Write-Host ''
Write-Host "Sky LIS package $version" -ForegroundColor Green
Write-Host "  repository : $RepositoryRoot"
Write-Host "  commit     : $commit ($commitDate)$(if ($dirty) { ' with uncommitted changes' })"
Write-Host "  output     : $packageRoot"
Write-Host "  tools      : dotnet SDK $dotnetSdk, node $nodeVersion, npm $npmVersion"
Write-Host "  build slot : $BuildSlot"
Write-Host ''

if ($dirty -and -not $WhatIfPreference) { Write-Warning 'The working tree has uncommitted changes; the manifest records dirty = true.' }

# --- output folder ----------------------------------------------------------------------------------------------
if (Test-Path -LiteralPath $packageRoot) {
    if (-not $Clean) { throw "$packageRoot already exists. Use -Clean to rebuild it, or bump VERSION." }
    if ($PSCmdlet.ShouldProcess($packageRoot, 'Remove existing package folder')) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
}
if ($PSCmdlet.ShouldProcess($packageRoot, 'Create package folder')) { New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null }

# --- API --------------------------------------------------------------------------------------------------------
if (-not $SkipApi) {
    Invoke-Native 'Publish the API (framework-dependent, win-x64)' $RepositoryRoot 'dotnet' @(
        'publish', $webApiProject,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'false',
        '-o', $apiOut,
        "-p:LisBuildSlot=$BuildSlot",
        '-p:UseAppHost=true',
        '-p:GenerateDocumentationFile=false',
        '--nologo'
    )

    if (-not $WhatIfPreference) {
        # Never ship the development settings: they point at the development database and relax the cookie.
        foreach ($name in @('appsettings.Development.json', 'secrets.json', 'Platform.WebApi.staticwebassets.runtime.json')) {
            $candidate = Join-Path $apiOut $name
            if (Test-Path -LiteralPath $candidate) { Remove-Item -LiteralPath $candidate -Force; Write-Host "  removed $name" }
        }
        $sensitiveExtensions = @('.json', '.pfx', '.pem', '.key', '.p12')
        foreach ($file in (Get-ChildItem -LiteralPath $apiOut -Recurse -File | Where-Object { $sensitiveExtensions -contains $_.Extension.ToLowerInvariant() })) {
            if ($file.Extension -ne '.json') { throw "The API output contains a key material file: $($file.FullName)." }
            $text = Get-Content -LiteralPath $file.FullName -Raw
            if ($text -match '(?i)password\s*=' -or $text -match '"SigningKey"\s*:\s*"[^"<]' -or $text -match '"MasterKey"\s*:\s*"[^"<]') {
                throw "The API output contains a secret-bearing value: $($file.FullName)."
            }
        }
        if (-not (Test-Path -LiteralPath (Join-Path $apiOut 'Platform.WebApi.dll'))) { throw 'dotnet publish produced no Platform.WebApi.dll.' }
        $size = Get-FolderSize $apiOut
        Write-Host ("  api: {0} files, {1:N1} MB" -f $size.Files, ($size.Bytes / 1MB))
    }
}

# --- DevTools (seed-platform-admin on the server, where no SDK is installed) ---------------------------------------
if (-not $SkipTools) {
    Invoke-Native 'Publish Platform.DevTools (framework-dependent, win-x64)' $RepositoryRoot 'dotnet' @(
        'publish', $devToolsProject,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'false',
        '-o', $toolsOut,
        "-p:LisBuildSlot=$BuildSlot",
        '-p:UseAppHost=true',
        '--nologo'
    )
    if (-not $WhatIfPreference) {
        foreach ($file in Get-ChildItem -LiteralPath $toolsOut -File | Where-Object { $_.Name -like 'appsettings*.json' -or $_.Name -eq 'secrets.json' }) { Remove-Item -LiteralPath $file.FullName -Force; Write-Host "  removed $($file.Name)" }
        if (-not (Test-Path -LiteralPath (Join-Path $toolsOut 'Platform.DevTools.exe'))) { throw 'dotnet publish produced no Platform.DevTools.exe.' }
        $size = Get-FolderSize $toolsOut
        Write-Host ("  tools: {0} files, {1:N1} MB" -f $size.Files, ($size.Bytes / 1MB))
    }
}

# --- SPA --------------------------------------------------------------------------------------------------------
if (-not $SkipUi) {
    if (-not $SkipNpmCi) {
        # npm ci deletes node_modules first; a running dev server (ng serve) keeps esbuild.exe open and the deletion
        # fails half-way, leaving a broken node_modules behind. Refuse before touching anything.
        $esbuild = Join-Path $frontend 'node_modules\@esbuild\win32-x64\esbuild.exe'
        if ((Test-Path -LiteralPath $esbuild) -and (Test-FileLocked $esbuild)) {
            throw "$esbuild is in use (a frontend dev server or test runner is running in $frontend). Stop it, or pass -SkipNpmCi to build with the existing node_modules."
        }
        Invoke-Native 'Install frontend packages (npm ci)' $frontend 'npm' @('ci', '--no-audit', '--no-fund')
    }
    Invoke-Native 'Build the SPA (production configuration)' $frontend 'npm' @('run', 'build', '--', '--configuration', 'production')

    if (-not $WhatIfPreference) {
        $browser = Join-Path $frontend 'dist\platform-web\browser'
        if (-not (Test-Path -LiteralPath (Join-Path $browser 'index.html'))) { throw "The SPA build produced no index.html under $browser." }
        New-Item -ItemType Directory -Path $uiOut -Force | Out-Null
        Copy-Item -Path (Join-Path $browser '*') -Destination $uiOut -Recurse -Force
        $size = Get-FolderSize $uiOut
        Write-Host ("  ui: {0} files, {1:N1} MB" -f $size.Files, ($size.Bytes / 1MB))
    }
}

# --- manifest ---------------------------------------------------------------------------------------------------
if (-not $WhatIfPreference) {
    $manifest = [ordered]@{
        product     = 'Sky LIS'
        version     = $version
        commit      = $commit
        commitDate  = $commitDate
        dirty       = $dirty
        builtAtUtc  = [DateTime]::UtcNow.ToString('o')
        builtOn     = $env:COMPUTERNAME
        toolchain   = [ordered]@{ dotnetSdk = $dotnetSdk; node = $nodeVersion; npm = $npmVersion }
        api         = $null
        tools       = $null
        ui          = $null
    }
    if (-not $SkipApi) { $s = Get-FolderSize $apiOut; $manifest.api = [ordered]@{ path = 'api'; entryPoint = 'Platform.WebApi.exe'; files = $s.Files; bytes = $s.Bytes } }
    if (-not $SkipTools) { $s = Get-FolderSize $toolsOut; $manifest.tools = [ordered]@{ path = 'tools'; entryPoint = 'Platform.DevTools.exe'; files = $s.Files; bytes = $s.Bytes } }
    if (-not $SkipUi) { $s = Get-FolderSize $uiOut; $manifest.ui = [ordered]@{ path = 'ui'; entryPoint = 'index.html'; files = $s.Files; bytes = $s.Bytes } }
    $manifestPath = Join-Path $packageRoot 'manifest.json'
    [System.IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 4) + "`n"), (New-Object System.Text.UTF8Encoding($false)))
    Write-Step "Manifest written: $manifestPath"
    Write-Host ''
    Write-Host "Package $version ready under $packageRoot" -ForegroundColor Green
    Write-Host '  next: deploy\windows\install-api-service.ps1 and deploy\windows\install-ui-site.ps1 on the server (docs\deploy\Release-1_Deployment_Runbook.md)'
} else {
    Write-Host 'WhatIf: nothing was built or written.' -ForegroundColor Yellow
}
