<#
.SYNOPSIS
    Installs or updates the Sky LIS API Windows service NTLIS (or NTLIS_TEST with -Test) from a published package.

.DESCRIPTION
    Layout (CLAUDE.md section 2, install root C:\lis-app):
        C:\lis-app\<service>\app                        the running build (replaced on every install)
        C:\lis-app\<service>\app.previous               the build before it (-Rollback swaps it back)
        C:\lis-app\<service>\config\appsettings.Production.json
                                                        the only configuration file, filled from
                                                        deploy\windows\appsettings.Production.template.json; it holds the
                                                        secrets of deploy\SECRETS.md and is readable by SYSTEM,
                                                        Administrators and the service account only
    The service runs as the virtual account NT SERVICE\<service> (no password to manage), delayed-auto start, restart
    on failure, with ASPNETCORE_ENVIRONMENT=Production pinned in its own environment block so that it never inherits
    another application's machine-wide value. Kestrel stays on loopback (5100 production, 5180 test); IIS proxies.

    The script is idempotent and supports -WhatIf (prints the plan, touches nothing). It refuses a package whose host
    is not service-aware (Microsoft.Extensions.Hosting.WindowsServices.dll missing), a configuration that still carries
    a <SET: ...> placeholder, a non-loopback Kestrel endpoint, a database or role outside the lis* / lis_* names,
    AllowInsecureLocalDevelopment = true, or any CredentialEnvelope:Mode key.

.PARAMETER PackagePath
    The published API folder (publish\<version>\api; contains Platform.WebApi.exe).
.PARAMETER ConfigSource
    A filled appsettings.Production.json to install into config\ (first installation, or a configuration change).
    Without it the existing config\appsettings.Production.json is used.
.PARAMETER Test
    Install NTLIS_TEST on port 5180 instead of NTLIS on 5100.
.PARAMETER InstallRoot
    Default C:\lis-app.
.PARAMETER Rollback
    Swap app.previous back into place and restart (no package needed).
.PARAMETER NoStart
    Install without starting the service.
.PARAMETER HealthTimeoutSeconds
    How long to wait for /health/ready after the start (default 120).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\install-api-service.ps1 -PackagePath E:\LIS\publish\1.0.0\api -ConfigSource E:\lis-data\staging\appsettings.Production.json -WhatIf
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\install-api-service.ps1 -PackagePath E:\LIS\publish\1.0.0\api -Test
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\install-api-service.ps1 -Rollback
#>
[CmdletBinding(SupportsShouldProcess = $true, DefaultParameterSetName = 'Install')]
param(
    [Parameter(ParameterSetName = 'Install', Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PackagePath,

    [Parameter(ParameterSetName = 'Install')]
    [string] $ConfigSource,

    [Parameter(ParameterSetName = 'Rollback', Mandatory = $true)]
    [switch] $Rollback,

    [switch] $Test,

    [ValidatePattern('^[A-Za-z]:\\[^\s"<>|*?]+$')]
    [string] $InstallRoot = 'C:\lis-app',

    [switch] $NoStart,

    [ValidateRange(10, 900)]
    [int] $HealthTimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- names (CLAUDE.md section 2) ----------------------------------------------------------------------------------
$serviceName = if ($Test) { 'NTLIS_TEST' } else { 'NTLIS' }
$port = if ($Test) { 5180 } else { 5100 }
$displayName = if ($Test) { 'Sky LIS API (test)' } else { 'Sky LIS API' }
$serviceAccount = "NT SERVICE\$serviceName"
$root = Join-Path $InstallRoot $serviceName
$appDir = Join-Path $root 'app'
$previousDir = Join-Path $root 'app.previous'
$configDir = Join-Path $root 'config'
$configFile = Join-Path $configDir 'appsettings.Production.json'
$exe = Join-Path $appDir 'Platform.WebApi.exe'
$healthUrl = "http://127.0.0.1:$port/health/ready"

function Write-Step([string] $Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script from an elevated (Administrator) PowerShell.'
    }
}

function Invoke-Sc([string[]] $Arguments) {
    # sc.exe is used for create/config because it accepts a virtual account without a password and sets the
    # failure actions; New-Service cannot do either.
    Write-Host "    sc.exe $($Arguments -join ' ')"
    $output = & sc.exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "sc.exe $($Arguments[0]) failed with code $LASTEXITCODE`: $($output -join ' ')" }
    return $output
}

function Wait-InstallReleased([string] $Root, [int] $Seconds = 60) {
    # The SCM reports Stopped before the process has exited and released its files.
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase) })
        if ($running.Count -eq 0) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "A process of $Root is still running after $Seconds s; stop it and run the installer again."
}

function Move-FolderWithRetry([string] $Source, [string] $Destination, [int] $Attempts = 10) {
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Move-Item -LiteralPath $Source -Destination $Destination -ErrorAction Stop
            return
        } catch [System.IO.IOException] {
            if ($attempt -eq $Attempts) { throw }
            Start-Sleep -Seconds 2
        }
    }
}

function Get-ServiceOrNull([string] $Name) {
    return Get-Service -Name $Name -ErrorAction SilentlyContinue
}

function Get-ServiceSid([string] $Name) {
    $output = & sc.exe showsid $Name
    $line = ($output | Where-Object { $_ -like '*SERVICE SID:*' } | Select-Object -First 1)
    if (-not $line -or ($line -notmatch 'SERVICE SID:\s*(S-1-5-80-[\d-]+)')) { throw "sc.exe showsid $Name returned no SID." }
    return New-Object System.Security.Principal.SecurityIdentifier($Matches[1])
}

function Set-ProtectedAcl {
    param([string] $Path, [System.Security.Principal.SecurityIdentifier] $ServiceSid, [System.Security.AccessControl.FileSystemRights] $ServiceRights)
    # SYSTEM and Administrators keep full control; the service account gets exactly $ServiceRights; nothing is inherited.
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.Access)) { [void] $acl.RemoveAccessRule($rule) }
    $isDirectory = (Get-Item -LiteralPath $Path) -is [System.IO.DirectoryInfo]
    $inherit = if ($isDirectory) { [System.Security.AccessControl.InheritanceFlags] 'ContainerInherit, ObjectInherit' } else { [System.Security.AccessControl.InheritanceFlags]::None }
    $system = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    $admins = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
    foreach ($entry in @(@($system, 'FullControl'), @($admins, 'FullControl'), @($ServiceSid, $ServiceRights))) {
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($entry[0], [System.Security.AccessControl.FileSystemRights] $entry[1], $inherit, [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow)))
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Get-JsonProperty($Object, [string[]] $Path) {
    $current = $Object
    foreach ($segment in $Path) {
        if ($null -eq $current) { return $null }
        $property = $current.PSObject.Properties[$segment]
        if ($null -eq $property) { return $null }
        $current = $property.Value
    }
    return $current
}

function Test-ProductionConfig([string] $Path) {
    # Mirrors the start-up guards (StartupGuards / ConfigGuard / DatabaseRoleGuard) so a bad file is caught before the
    # service is touched. Nothing from the file is printed.
    $text = Get-Content -LiteralPath $Path -Raw
    $problems = New-Object System.Collections.Generic.List[string]
    if ($text -match '<SET:') { $problems.Add('a <SET: ...> placeholder of the template is still present') }
    if ($text -match '(?i)CredentialEnvelope') { $problems.Add('a CredentialEnvelope key is present; the credential envelope has no optional mode') }
    try { $json = $text | ConvertFrom-Json } catch { throw "$Path is not valid JSON: $($_.Exception.Message)" }
    foreach ($generic in @('ConnectionStrings', 'Jwt', 'DataProtection', 'urls', 'ASPNETCORE_URLS')) {
        if ($null -ne $json.PSObject.Properties[$generic]) { $problems.Add("top-level '$generic' is a generic key of another application; LIS reads only the Lis root") }
    }
    if ($null -eq $json.PSObject.Properties['Lis']) { $problems.Add("no 'Lis' section") }

    $relaxed = Get-JsonProperty $json @('Lis', 'Security', 'AllowInsecureLocalDevelopment')
    if ($relaxed -eq $true) { $problems.Add('Lis:Security:AllowInsecureLocalDevelopment must be false on a server') }

    $connection = Get-JsonProperty $json @('Lis', 'Database', 'ConnectionString')
    if (-not $connection) {
        $problems.Add('Lis:Database:ConnectionString is missing')
    } else {
        $pairs = @{}
        foreach ($pair in ($connection -split ';')) { if ($pair -match '^\s*([^=]+?)\s*=\s*(.*?)\s*$') { $pairs[$Matches[1].ToLowerInvariant()] = $Matches[2] } }
        $database = if ($pairs.ContainsKey('database')) { $pairs['database'] } else { '' }
        $user = if ($pairs.ContainsKey('username')) { $pairs['username'] } elseif ($pairs.ContainsKey('user id')) { $pairs['user id'] } else { '' }
        if ($database -notmatch '^(?i)lis') { $problems.Add("database '$database' is not an LIS database (name must start with 'lis')") }
        if ($user -notmatch '^(?i)lis_') { $problems.Add("role '$user' is not an LIS role (name must start with 'lis_')") }
    }

    $url = Get-JsonProperty $json @('Kestrel', 'Endpoints', 'Http', 'Url')
    if ($url) {
        if ($url -notmatch "^http://(127\.0\.0\.1|localhost|\[::1\]):$port/?$") { $problems.Add("Kestrel:Endpoints:Http:Url is '$url'; expected http://127.0.0.1:$port (loopback, the port of $serviceName)") }
    } elseif ($Test) {
        $problems.Add("Kestrel:Endpoints:Http:Url must be set to http://127.0.0.1:$port for $serviceName (appsettings.json pins 5100)")
    }

    foreach ($required in @(@('Lis', 'Jwt', 'Issuer'), @('Lis', 'Jwt', 'SigningKey'), @('Lis', 'Jwt', 'SubjectEncryptionKey'), @('Lis', 'DataProtection', 'MasterKey'))) {
        if (-not (Get-JsonProperty $json $required)) { $problems.Add("$($required -join ':') is missing") }
    }

    # Lis:Notifications:PickupDirectory holds one-time codes in clear text: an absolute folder under the LIS data root (ConfigGuard).
    $pickup = Get-JsonProperty $json @('Lis', 'Notifications', 'PickupDirectory')
    if ($pickup -and ($pickup -notmatch '^(?i)E:\\lis-data\\[^\\]')) { $problems.Add("Lis:Notifications:PickupDirectory '$pickup' must be an absolute folder under E:\lis-data") }

    if ($problems.Count -gt 0) { throw "config\appsettings.Production.json is refused:`n - " + ($problems -join "`n - ") }

    $filesRoot = Get-JsonProperty $json @('Lis', 'Files', 'Root')
    return [pscustomobject]@{ FilesRoot = $filesRoot; PickupDirectory = $pickup }
}

function Wait-Ready([string] $Url, [int] $TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $last = ''
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return $true }
            $last = "HTTP $($response.StatusCode)"
        } catch {
            $last = $_.Exception.Message
        }
        Start-Sleep -Seconds 3
    }
    Write-Warning "last probe result: $last"
    return $false
}

function Start-ServiceAndProbe {
    if ($NoStart) { Write-Host "  -NoStart: $serviceName left stopped."; return }
    if ($PSCmdlet.ShouldProcess($serviceName, 'Start service')) {
        Start-Service -Name $serviceName
        Write-Step "Waiting for $healthUrl (up to $HealthTimeoutSeconds s)"
        if (Wait-Ready $healthUrl $HealthTimeoutSeconds) {
            Write-Host "  $serviceName is ready on 127.0.0.1:$port" -ForegroundColor Green
        } else {
            $state = (Get-ServiceOrNull $serviceName).Status
            throw "$serviceName did not become ready (service state: $state). Check the Application event log (source $serviceName / .NET Runtime) and config\appsettings.Production.json; roll back with: install-api-service.ps1 -Rollback$(if ($Test) { ' -Test' })"
        }
    }
}

# --- plan -------------------------------------------------------------------------------------------------------
Write-Host ''
Write-Host "Sky LIS API service: $serviceName" -ForegroundColor Green
Write-Host "  install root : $root"
Write-Host "  account      : $serviceAccount"
Write-Host "  endpoint     : http://127.0.0.1:$port (loopback; IIS proxies /api, /health, /.well-known)"
Write-Host "  mode         : $($PSCmdlet.ParameterSetName)$(if ($WhatIfPreference) { ' (WhatIf: nothing is changed)' })"
Write-Host ''

if (-not $WhatIfPreference) { Assert-Administrator }

$existing = Get-ServiceOrNull $serviceName

# --- rollback ---------------------------------------------------------------------------------------------------
if ($PSCmdlet.ParameterSetName -eq 'Rollback') {
    if (-not (Test-Path -LiteralPath (Join-Path $previousDir 'Platform.WebApi.exe'))) { throw "$previousDir holds no previous build to roll back to." }
    if (-not $existing) { throw "Service $serviceName does not exist." }
    if ($PSCmdlet.ShouldProcess($serviceName, 'Stop service')) { if ($existing.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force; $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60)) }; Wait-InstallReleased $root }
    $swap = Join-Path $root 'app.swap'
    if ($PSCmdlet.ShouldProcess("$appDir <-> $previousDir", 'Swap current and previous build')) {
        if (Test-Path -LiteralPath $swap) { Remove-Item -LiteralPath $swap -Recurse -Force }
        Move-FolderWithRetry $appDir $swap
        Move-Item -LiteralPath $previousDir -Destination $appDir
        Move-Item -LiteralPath $swap -Destination $previousDir
        Copy-Item -LiteralPath $configFile -Destination (Join-Path $appDir 'appsettings.Production.json') -Force
    }
    Start-ServiceAndProbe
    Write-Host "Rolled $serviceName back to the previous build." -ForegroundColor Green
    return
}

# --- validate the package ---------------------------------------------------------------------------------------
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
foreach ($required in @('Platform.WebApi.exe', 'Platform.WebApi.dll', 'Platform.WebApi.runtimeconfig.json', 'appsettings.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PackagePath $required))) { throw "$PackagePath is not a published API package: $required is missing. Build one with deploy\windows\publish.ps1." }
}
if (Test-Path -LiteralPath (Join-Path $PackagePath 'appsettings.Development.json')) { throw "$PackagePath contains appsettings.Development.json; publish.ps1 removes it. Do not install a hand-made package." }
if (-not (Test-Path -LiteralPath (Join-Path $PackagePath 'Microsoft.Extensions.Hosting.WindowsServices.dll'))) {
    throw "The host in $PackagePath is not service-aware: Microsoft.Extensions.Hosting.WindowsServices.dll is missing, so the Service Control Manager would stop it with error 1053. The WebApi must call builder.Host.UseWindowsService() (package Microsoft.Extensions.Hosting.WindowsServices) before it can run as $serviceName. See the runbook, section 'Prerequisites'."
}
$manifestPath = Join-Path (Split-Path -Parent $PackagePath) 'manifest.json'
$packageVersion = '(no manifest)'
if (Test-Path -LiteralPath $manifestPath) { $m = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json; $packageVersion = "$($m.version) commit $($m.commit)" }
Write-Host "  package      : $PackagePath ($packageVersion)"

$runtimes = & dotnet --list-runtimes 2>$null
if (-not ($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 9\.' })) { throw 'The ASP.NET Core Runtime 9.0 is not installed (dotnet --list-runtimes shows no Microsoft.AspNetCore.App 9.x). Install the ASP.NET Core 9 Hosting Bundle or Runtime.' }

# --- validate the configuration ----------------------------------------------------------------------------------
$configToValidate = $null
if ($ConfigSource) {
    $ConfigSource = (Resolve-Path -LiteralPath $ConfigSource).Path
    $configToValidate = $ConfigSource
} elseif (Test-Path -LiteralPath $configFile) {
    $configToValidate = $configFile
} else {
    throw "No configuration: pass -ConfigSource <filled appsettings.Production.json> on the first installation (template: deploy\windows\appsettings.Production.template.json; secrets: deploy\SECRETS.md). It will be installed as $configFile."
}
Write-Step "Validating $configToValidate"
$config = Test-ProductionConfig $configToValidate
Write-Host '  configuration accepted'

# --- folders and configuration -----------------------------------------------------------------------------------
foreach ($dir in @($root, $configDir)) {
    if (-not (Test-Path -LiteralPath $dir)) { if ($PSCmdlet.ShouldProcess($dir, 'Create folder')) { New-Item -ItemType Directory -Path $dir -Force | Out-Null } }
}
if ($ConfigSource -and ($ConfigSource -ne $configFile)) {
    if ($PSCmdlet.ShouldProcess($configFile, "Install configuration from $ConfigSource")) { Copy-Item -LiteralPath $ConfigSource -Destination $configFile -Force }
}

# --- stop, rotate, copy --------------------------------------------------------------------------------------------
if ($existing -and $existing.Status -ne 'Stopped') {
    if ($PSCmdlet.ShouldProcess($serviceName, 'Stop service')) { Stop-Service -Name $serviceName -Force; $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60)); Wait-InstallReleased $root }
}
if (Test-Path -LiteralPath $appDir) {
    if ($PSCmdlet.ShouldProcess($appDir, "Keep as $previousDir and replace with the package")) {
        if (Test-Path -LiteralPath $previousDir) { Remove-Item -LiteralPath $previousDir -Recurse -Force }
        Move-FolderWithRetry $appDir $previousDir
    }
}
if ($PSCmdlet.ShouldProcess($appDir, "Copy package $PackagePath")) {
    New-Item -ItemType Directory -Path $appDir -Force | Out-Null
    Copy-Item -Path (Join-Path $PackagePath '*') -Destination $appDir -Recurse -Force
    Copy-Item -LiteralPath $configFile -Destination (Join-Path $appDir 'appsettings.Production.json') -Force
}

# --- service ------------------------------------------------------------------------------------------------------
$binPath = "`"$exe`""
if (-not $existing) {
    if ($PSCmdlet.ShouldProcess($serviceName, "Create service ($binPath, $serviceAccount, delayed-auto)")) {
        Invoke-Sc @('create', $serviceName, 'binPath=', $binPath, 'start=', 'delayed-auto', 'obj=', $serviceAccount, 'DisplayName=', $displayName) | Out-Null
    }
} else {
    if ($PSCmdlet.ShouldProcess($serviceName, "Update service ($binPath, $serviceAccount, delayed-auto)")) {
        Invoke-Sc @('config', $serviceName, 'binPath=', $binPath, 'start=', 'delayed-auto', 'obj=', $serviceAccount, 'DisplayName=', $displayName) | Out-Null
    }
}
if ($PSCmdlet.ShouldProcess($serviceName, 'Set description, failure actions and environment')) {
    Invoke-Sc @('description', $serviceName, "Sky LIS API ($serviceName) on http://127.0.0.1:$port, reverse-proxied by IIS. Configuration: $configFile") | Out-Null
    Invoke-Sc @('failure', $serviceName, 'reset=', '86400', 'actions=', 'restart/5000/restart/10000/restart/30000') | Out-Null
    Invoke-Sc @('failureflag', $serviceName, '1') | Out-Null
    # The service's own environment block: pins the environment name so appsettings.Production.json is the file loaded,
    # whatever ASPNETCORE_ENVIRONMENT another application sets machine-wide. No secret is stored here.
    $registry = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
    Set-ItemProperty -LiteralPath $registry -Name 'Environment' -Type MultiString -Value @('ASPNETCORE_ENVIRONMENT=Production', 'DOTNET_ENVIRONMENT=Production', 'DOTNET_CLI_TELEMETRY_OPTOUT=1')
}

# --- permissions ---------------------------------------------------------------------------------------------------
if ($PSCmdlet.ShouldProcess("$root, $configFile, files root, mail pickup folder", "Grant $serviceAccount read on the build and configuration, modify on the files root and the mail pickup folder")) {
    $sid = Get-ServiceSid $serviceName
    Set-ProtectedAcl -Path $appDir -ServiceSid $sid -ServiceRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
    Set-ProtectedAcl -Path $configDir -ServiceSid $sid -ServiceRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
    Set-ProtectedAcl -Path $configFile -ServiceSid $sid -ServiceRights ([System.Security.AccessControl.FileSystemRights]::Read)
    Set-ProtectedAcl -Path (Join-Path $appDir 'appsettings.Production.json') -ServiceSid $sid -ServiceRights ([System.Security.AccessControl.FileSystemRights]::Read)
    if ($config.FilesRoot) {
        if (-not (Test-Path -LiteralPath $config.FilesRoot)) { New-Item -ItemType Directory -Path $config.FilesRoot -Force | Out-Null }
        Set-ProtectedAcl -Path $config.FilesRoot -ServiceSid $sid -ServiceRights ([System.Security.AccessControl.FileSystemRights]::Modify)
    }
    if ($config.PickupDirectory) {
        # One file per notification, one-time codes included: SYSTEM, Administrators and the service account only, nothing inherited.
        if (-not (Test-Path -LiteralPath $config.PickupDirectory)) { New-Item -ItemType Directory -Path $config.PickupDirectory -Force | Out-Null }
        Set-ProtectedAcl -Path $config.PickupDirectory -ServiceSid $sid -ServiceRights ([System.Security.AccessControl.FileSystemRights]::Modify)
    }
}

Start-ServiceAndProbe

Write-Host ''
Write-Host "$serviceName installed from $PackagePath." -ForegroundColor Green
Write-Host "  previous build kept in $previousDir (rollback: install-api-service.ps1 -Rollback$(if ($Test) { ' -Test' }))"
Write-Host "  next: deploy\windows\install-ui-site.ps1$(if ($Test) { ' -Test' }) and deploy\windows\smoke-test.ps1"
