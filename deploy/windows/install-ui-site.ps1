<#
.SYNOPSIS
    Installs or updates the IIS site lis-ui (or lis-ui-test with -Test) that serves the Sky LIS SPA and reverse-proxies
    the API.

.DESCRIPTION
    Site lis-ui at C:\inetpub\wwwroot\lis-ui on its own application pool lis-ui (no managed code), binding
    https://<HostHeader>:443 with the given certificate (SNI). With -Test: site lis-ui-test at
    C:\inetpub\wwwroot\lis-ui-test, pool lis-ui-test, http on port 8081 (or https when a certificate is given),
    proxying to the test API on 5180. The web.config comes from deploy\windows\web.config with the API port and the
    CSP hashes of the inline scripts of index.html filled in; it rewrites every non-file URL to index.html, proxies
    /api, /health and /.well-known to the loopback API (same origin, so the refresh cookie works), sets the security
    headers and the caching rules (index.html no-store, hashed bundles immutable).

    Prerequisites checked: IIS with the WebAdministration module, URL Rewrite 2.x (global module RewriteModule),
    Application Request Routing 3 (global module ApplicationRequestRouting) with the proxy enabled at server level,
    and HTTP_X_FORWARDED_PROTO in the server-level <allowedServerVariables>. The script never changes a site, pool or
    binding that is not named lis-ui*; it refuses a binding another site already owns. The two server-level settings
    it can change (ARR proxy enabled, the allowed server variable) are additive, apply to this server's rewrite engine
    and are changed only under -EnableArrProxy / ShouldProcess.

    The previous content is kept next to the site as <path>.previous; -Rollback swaps it back. Idempotent; -WhatIf
    prints the plan and touches nothing.

.PARAMETER PackagePath
    The published SPA folder (publish\<version>\ui; contains index.html).
.PARAMETER HostHeader
    Host name of the binding (required for the production site; optional for -Test).
.PARAMETER CertificateThumbprint
    Thumbprint of a certificate in Cert:\LocalMachine\My for the https binding (required for the production site
    unless -AllowHttp; without it the refresh cookie __Secure-rt cannot be set).
.PARAMETER Port
    Binding port: default 443 (production) or 8081 (test).
.PARAMETER Test
    Install lis-ui-test (port 8081, API 5180).
.PARAMETER AllowHttp
    Accept an http binding for the production site (smoke tests only; sign-in cannot persist without TLS).
.PARAMETER EnableArrProxy
    Enable the ARR proxy at server level when it is disabled (otherwise the script stops and prints the command).
.PARAMETER Rollback
    Swap <path>.previous back into place.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\install-ui-site.ps1 -PackagePath E:\LIS\publish\1.0.0\ui -HostHeader lis.example.net -CertificateThumbprint 0123... -WhatIf
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\install-ui-site.ps1 -PackagePath E:\LIS\publish\1.0.0\ui -Test
#>
[CmdletBinding(SupportsShouldProcess = $true, DefaultParameterSetName = 'Install')]
param(
    [Parameter(ParameterSetName = 'Install', Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PackagePath,

    [Parameter(ParameterSetName = 'Rollback', Mandatory = $true)]
    [switch] $Rollback,

    [ValidatePattern('^[A-Za-z0-9.-]*$')]
    [string] $HostHeader = '',

    [ValidatePattern('^[0-9A-Fa-f]*$')]
    [string] $CertificateThumbprint = '',

    [ValidateRange(1, 65535)]
    [int] $Port = 0,

    [switch] $Test,
    [switch] $AllowHttp,
    [switch] $EnableArrProxy,

    [ValidatePattern('^[A-Za-z]:\\[^\s"<>|*?]+$')]
    [string] $SiteRoot = 'C:\inetpub\wwwroot',

    [string] $WebConfigTemplate = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ($PSScriptRoot is empty inside param() defaults under Windows PowerShell 5.1, so the default is resolved here.)
if ($WebConfigTemplate -eq '') { $WebConfigTemplate = Join-Path $PSScriptRoot 'web.config' }

# --- names (CLAUDE.md section 2) ----------------------------------------------------------------------------------
$siteName = if ($Test) { 'lis-ui-test' } else { 'lis-ui' }
$poolName = $siteName
$apiPort = if ($Test) { 5180 } else { 5100 }
if ($Port -eq 0) { $Port = if ($Test) { 8081 } else { 443 } }
$physicalPath = Join-Path $SiteRoot $siteName
$previousPath = "$physicalPath.previous"
$stagingPath = "$physicalPath.staging"
$useTls = $CertificateThumbprint -ne ''
$protocol = if ($useTls) { 'https' } else { 'http' }
$poolIdentity = "IIS AppPool\$poolName"

function Write-Step([string] $Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated (Administrator) PowerShell.' }
}

function Get-InlineScriptHashes([string] $IndexHtml) {
    # CSP 'sha256-...' sources for the inline scripts the Angular build writes into index.html (critical CSS loader).
    $html = Get-Content -LiteralPath $IndexHtml -Raw
    $hashes = New-Object System.Collections.Generic.List[string]
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        foreach ($match in [regex]::Matches($html, '<script(?![^>]*\bsrc\s*=)[^>]*>(.*?)</script>', 'Singleline, IgnoreCase')) {
            $body = $match.Groups[1].Value
            if ($body.Trim().Length -eq 0) { continue }
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
            $hashes.Add("'sha256-" + [Convert]::ToBase64String($sha.ComputeHash($bytes)) + "'")
        }
    } finally { $sha.Dispose() }
    return $hashes
}

function New-SiteWebConfig([string] $Template, [string] $IndexHtml) {
    $text = Get-Content -LiteralPath $Template -Raw
    foreach ($token in @('__API_PORT__', '__SCRIPT_HASHES__')) { if ($text -notlike "*$token*") { throw "$Template does not contain the token $token." } }
    $hashes = @(Get-InlineScriptHashes $IndexHtml)
    $text = $text.Replace('__API_PORT__', [string] $apiPort)
    $text = $text.Replace(" __SCRIPT_HASHES__", $(if ($hashes.Count -gt 0) { ' ' + ($hashes -join ' ') } else { '' }))
    return [pscustomobject]@{ Text = $text; Hashes = $hashes.Count }
}

function Get-AppHostProperty([string] $Filter, [string] $Name) {
    return Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $Filter -Name $Name
}

function Set-PoolIdentityAcl([string] $Path) {
    $acl = Get-Acl -LiteralPath $Path
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($poolIdentity, [System.Security.AccessControl.FileSystemRights]::ReadAndExecute, 'ContainerInherit, ObjectInherit', 'None', 'Allow')
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Find-BindingOwner([string] $Protocol, [int] $BindingPort, [string] $HostName) {
    # Another site that already listens on the same protocol/port/host header; never altered, only reported.
    foreach ($site in Get-Website) {
        if ($site.Name -eq $siteName) { continue }
        foreach ($binding in @($site.bindings.Collection)) {
            if ($binding.protocol -ne $Protocol) { continue }
            $parts = $binding.bindingInformation -split ':'
            if ($parts.Length -lt 3) { continue }
            if ([int] $parts[1] -eq $BindingPort -and $parts[2] -eq $HostName) { return $site.Name }
        }
    }
    return $null
}

# --- plan -------------------------------------------------------------------------------------------------------
Write-Host ''
Write-Host "Sky LIS site: $siteName" -ForegroundColor Green
Write-Host "  physical path : $physicalPath"
Write-Host "  app pool      : $poolName (no managed code, $poolIdentity)"
Write-Host "  binding       : ${protocol}://$(if ($HostHeader) { $HostHeader } else { '*' }):$Port$(if ($useTls) { " (certificate $CertificateThumbprint, SNI)" })"
Write-Host "  API upstream  : http://127.0.0.1:$apiPort (/api, /health, /.well-known)"
Write-Host "  mode          : $($PSCmdlet.ParameterSetName)$(if ($WhatIfPreference) { ' (WhatIf: nothing is changed)' })"
Write-Host ''

if (-not $Test -and -not $HostHeader) { throw 'The production site needs -HostHeader (the public host name of the portal).' }
if (-not $Test -and -not $useTls -and -not $AllowHttp) { throw 'The production site needs -CertificateThumbprint: without TLS the browser refuses the __Secure-rt refresh cookie. Use -AllowHttp for a smoke-only binding.' }

if (-not $WhatIfPreference) { Assert-Administrator }
Import-Module WebAdministration -ErrorAction Stop

# --- prerequisites ------------------------------------------------------------------------------------------------
Write-Step 'Checking IIS prerequisites'
$globalModules = @(Get-WebGlobalModule | Select-Object -ExpandProperty Name)
if ($globalModules -notcontains 'RewriteModule') { throw 'URL Rewrite is not installed (global module RewriteModule missing). Install "URL Rewrite 2.1" from the IIS downloads.' }
if ($globalModules -notcontains 'ApplicationRequestRouting') { throw 'Application Request Routing is not installed (global module ApplicationRequestRouting missing). Install "Application Request Routing 3.0".' }
Write-Host '  URL Rewrite and ARR modules present'

$proxyEnabled = [bool] (Get-AppHostProperty 'system.webServer/proxy' 'enabled').Value
if (-not $proxyEnabled) {
    if (-not $EnableArrProxy) { throw 'The ARR proxy is disabled at server level. Re-run with -EnableArrProxy, or run: %windir%\system32\inetsrv\appcmd set config -section:system.webServer/proxy /enabled:"True" /commit:apphost' }
    if ($PSCmdlet.ShouldProcess('MACHINE/WEBROOT/APPHOST system.webServer/proxy', 'Enable the ARR proxy (server level)')) {
        Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' -Name 'enabled' -Value $true
    }
} else { Write-Host '  ARR proxy enabled' }

$allowed = @(Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/rewrite/allowedServerVariables/add' | Select-Object -ExpandProperty name)
if ($allowed -notcontains 'HTTP_X_FORWARDED_PROTO') {
    if ($PSCmdlet.ShouldProcess('MACHINE/WEBROOT/APPHOST system.webServer/rewrite/allowedServerVariables', 'Allow HTTP_X_FORWARDED_PROTO (server level, additive)')) {
        Add-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/rewrite/allowedServerVariables' -Value @{ name = 'HTTP_X_FORWARDED_PROTO' }
    }
} else { Write-Host '  HTTP_X_FORWARDED_PROTO allowed' }

$certificate = $null
if ($useTls) {
    $certificate = Get-ChildItem -Path 'Cert:\LocalMachine\My' | Where-Object { $_.Thumbprint -eq $CertificateThumbprint.ToUpperInvariant() } | Select-Object -First 1
    if (-not $certificate) { throw "No certificate with thumbprint $CertificateThumbprint in Cert:\LocalMachine\My." }
    if ($certificate.NotAfter -lt (Get-Date).AddDays(14)) { Write-Warning "The certificate expires on $($certificate.NotAfter)." }
    Write-Host "  certificate: $($certificate.Subject) (expires $($certificate.NotAfter.ToString('yyyy-MM-dd')))"
}

$owner = Find-BindingOwner $protocol $Port $HostHeader
if ($owner) { throw "Site '$owner' already binds ${protocol}://$(if ($HostHeader) { $HostHeader } else { '*' }):$Port. LIS never alters another site; choose another host header or port." }

# --- rollback -----------------------------------------------------------------------------------------------------
if ($PSCmdlet.ParameterSetName -eq 'Rollback') {
    if (-not (Test-Path -LiteralPath (Join-Path $previousPath 'index.html'))) { throw "$previousPath holds no previous build." }
    $swap = "$physicalPath.swap"
    if ($PSCmdlet.ShouldProcess("$physicalPath <-> $previousPath", 'Swap current and previous content')) {
        if (Test-Path -LiteralPath $swap) { Remove-Item -LiteralPath $swap -Recurse -Force }
        Move-Item -LiteralPath $physicalPath -Destination $swap
        Move-Item -LiteralPath $previousPath -Destination $physicalPath
        Move-Item -LiteralPath $swap -Destination $previousPath
    }
    Write-Host "Rolled $siteName back to the previous content." -ForegroundColor Green
    return
}

# --- package and web.config -----------------------------------------------------------------------------------------
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$indexHtml = Join-Path $PackagePath 'index.html'
if (-not (Test-Path -LiteralPath $indexHtml)) { throw "$PackagePath is not a published SPA package: index.html is missing. Build one with deploy\windows\publish.ps1." }
if (-not (Test-Path -LiteralPath $WebConfigTemplate)) { throw "web.config template not found: $WebConfigTemplate" }
$webConfig = New-SiteWebConfig $WebConfigTemplate $indexHtml
Write-Host "  web.config: API port $apiPort, $($webConfig.Hashes) inline script hash(es) in the CSP"

# --- content: stage, then swap ---------------------------------------------------------------------------------------
if ($PSCmdlet.ShouldProcess($physicalPath, "Install content from $PackagePath (previous kept as $previousPath)")) {
    if (Test-Path -LiteralPath $stagingPath) { Remove-Item -LiteralPath $stagingPath -Recurse -Force }
    New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
    Copy-Item -Path (Join-Path $PackagePath '*') -Destination $stagingPath -Recurse -Force
    [System.IO.File]::WriteAllText((Join-Path $stagingPath 'web.config'), $webConfig.Text, (New-Object System.Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $previousPath) { Remove-Item -LiteralPath $previousPath -Recurse -Force }
    if (Test-Path -LiteralPath $physicalPath) { Move-Item -LiteralPath $physicalPath -Destination $previousPath }
    Move-Item -LiteralPath $stagingPath -Destination $physicalPath
}

# --- application pool -----------------------------------------------------------------------------------------------
if (-not (Test-Path "IIS:\AppPools\$poolName")) {
    if ($PSCmdlet.ShouldProcess($poolName, 'Create application pool (no managed code, ApplicationPoolIdentity)')) { New-WebAppPool -Name $poolName | Out-Null }
}
if ($PSCmdlet.ShouldProcess($poolName, 'Configure application pool')) {
    Set-ItemProperty "IIS:\AppPools\$poolName" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$poolName" -Name managedPipelineMode -Value 'Integrated'
    Set-ItemProperty "IIS:\AppPools\$poolName" -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    Set-ItemProperty "IIS:\AppPools\$poolName" -Name autoStart -Value $true
}

# --- site and binding ------------------------------------------------------------------------------------------------
$bindingInformation = "*:${Port}:$HostHeader"
$site = Get-Website -Name $siteName -ErrorAction SilentlyContinue
if (-not $site) {
    if ($PSCmdlet.ShouldProcess($siteName, "Create site ($physicalPath, pool $poolName, ${protocol} $bindingInformation)")) {
        $arguments = @{ Name = $siteName; PhysicalPath = $physicalPath; ApplicationPool = $poolName; Port = $Port; HostHeader = $HostHeader }
        if ($useTls) { $arguments.Ssl = $true; $arguments.SslFlags = 1 }
        New-Website @arguments | Out-Null
    }
} else {
    if ($PSCmdlet.ShouldProcess($siteName, "Update site ($physicalPath, pool $poolName)")) {
        Set-ItemProperty "IIS:\Sites\$siteName" -Name physicalPath -Value $physicalPath
        Set-ItemProperty "IIS:\Sites\$siteName" -Name applicationPool -Value $poolName
    }
    $existingBinding = Get-WebBinding -Name $siteName -Protocol $protocol | Where-Object { $_.bindingInformation -eq $bindingInformation }
    if (-not $existingBinding) {
        if ($PSCmdlet.ShouldProcess($siteName, "Add binding ${protocol} $bindingInformation")) {
            if ($useTls) { New-WebBinding -Name $siteName -Protocol https -Port $Port -HostHeader $HostHeader -SslFlags 1 } else { New-WebBinding -Name $siteName -Protocol http -Port $Port -HostHeader $HostHeader }
        }
    }
}
if ($useTls) {
    if ($PSCmdlet.ShouldProcess("${HostHeader}:$Port", "Bind certificate $CertificateThumbprint")) {
        $sslPath = "IIS:\SslBindings\!${Port}!$HostHeader"
        if (Test-Path $sslPath) { Remove-Item $sslPath }
        New-Item $sslPath -Value $certificate -SSLFlags 1 | Out-Null
    }
}

if ($PSCmdlet.ShouldProcess($physicalPath, "Grant $poolIdentity read and execute")) { Set-PoolIdentityAcl $physicalPath }

if ($PSCmdlet.ShouldProcess($siteName, 'Start application pool and site')) {
    if ((Get-WebAppPoolState -Name $poolName).Value -ne 'Started') { Start-WebAppPool -Name $poolName }
    if ((Get-WebsiteState -Name $siteName).Value -ne 'Started') { Start-Website -Name $siteName }
}

Write-Host ''
Write-Host "$siteName installed from $PackagePath." -ForegroundColor Green
Write-Host "  previous content kept in $previousPath (rollback: install-ui-site.ps1 -Rollback$(if ($Test) { ' -Test' }))"
$baseUrl = "${protocol}://$(if ($HostHeader) { $HostHeader } else { 'localhost' })$(if (($useTls -and $Port -ne 443) -or (-not $useTls -and $Port -ne 80)) { ":$Port" })"
Write-Host "  next: deploy\windows\smoke-test.ps1 -BaseUrl $baseUrl"
