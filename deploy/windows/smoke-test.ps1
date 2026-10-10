<#
.SYNOPSIS
    Post-deployment smoke test of a Sky LIS site: health, JWKS, SPA and proxy behaviour through IIS.

.DESCRIPTION
    Requests, through the site (-BaseUrl), /health/live, /health/ready, /.well-known/jwks.json, the SPA index.html,
    a deep route (router fallback), the security and caching headers of the SPA, and an unauthenticated API call
    (expects 401, which proves /api is proxied and guarded). With -ApiUrl the same health endpoints are also probed
    directly on the loopback API. Prints one line per check and exits 0 when every check passed, 1 when any check
    failed (a parameter error fails before any check and exits non-zero as well). Read-only: it sends no credentials
    and changes nothing.

.PARAMETER BaseUrl
    The site's origin, e.g. https://lis.example.net or http://localhost:8081.
.PARAMETER ApiUrl
    Optional loopback API origin, e.g. http://127.0.0.1:5100.
.PARAMETER HostHeader
    Host header to send when -BaseUrl points at an IP or localhost but the site binds a host name.
.PARAMETER SkipCertificateCheck
    Accept an untrusted certificate (test sites with a self-signed certificate).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\smoke-test.ps1 -BaseUrl https://lis.example.net -ApiUrl http://127.0.0.1:5100
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\smoke-test.ps1 -BaseUrl http://localhost:8081 -ApiUrl http://127.0.0.1:5180
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https?://[^/\s]+/?$')]
    [string] $BaseUrl,

    [ValidatePattern('^(https?://[^/\s]+/?)?$')]
    [string] $ApiUrl = '',

    [ValidatePattern('^[A-Za-z0-9.:-]*$')]
    [string] $HostHeader = '',

    [switch] $SkipCertificateCheck,

    [ValidateRange(1, 120)]
    [int] $TimeoutSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BaseUrl = $BaseUrl.TrimEnd('/')
$ApiUrl = $ApiUrl.TrimEnd('/')
$script:failures = 0
$script:passes = 0

if ($SkipCertificateCheck) {
    # A script block callback fails on the background thread that validates the certificate ("no Runspace available");
    # a compiled delegate does not.
    Add-Type -TypeDefinition @"
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
public static class LisSmokeTrustAll { public static bool Validate(object s, X509Certificate c, X509Chain ch, SslPolicyErrors e) { return true; } }
"@
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = [System.Delegate]::CreateDelegate([System.Net.Security.RemoteCertificateValidationCallback], [LisSmokeTrustAll].GetMethod('Validate'))
}
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12

function Invoke-Probe([string] $Url) {
    # Returns status, headers and body without throwing on 4xx/5xx (Invoke-WebRequest throws on those in 5.1).
    $headers = @{}
    if ($HostHeader) { $headers['Host'] = $HostHeader }
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $TimeoutSeconds -Headers $headers -MaximumRedirection 0 -ErrorAction Stop
        return [pscustomobject]@{ Status = [int] $response.StatusCode; Headers = $response.Headers; Body = [string] $response.Content; Error = '' }
    } catch [System.Net.WebException] {
        $web = $_.Exception
        if ($web.Response) {
            $http = [System.Net.HttpWebResponse] $web.Response
            $reader = New-Object System.IO.StreamReader($http.GetResponseStream())
            $body = $reader.ReadToEnd()
            $headers = @{}
            foreach ($name in $http.Headers.AllKeys) { $headers[$name] = $http.Headers[$name] }
            return [pscustomobject]@{ Status = [int] $http.StatusCode; Headers = $headers; Body = $body; Error = '' }
        }
        return [pscustomobject]@{ Status = 0; Headers = @{}; Body = ''; Error = $web.Message }
    }
}

function Get-Header($Headers, [string] $Name) {
    foreach ($key in $Headers.Keys) { if ($key -ieq $Name) { return [string] $Headers[$key] } }
    return ''
}

function Check([string] $Name, [bool] $Passed, [string] $Detail) {
    if ($Passed) { $script:passes++; Write-Host ("  PASS  {0,-52} {1}" -f $Name, $Detail) -ForegroundColor Green }
    else { $script:failures++; Write-Host ("  FAIL  {0,-52} {1}" -f $Name, $Detail) -ForegroundColor Red }
}

function Test-Endpoint([string] $Origin, [string] $Path, [int] $ExpectedStatus, [string] $BodyMustContain, [string] $Label) {
    $probe = Invoke-Probe "$Origin$Path"
    if ($probe.Status -eq 0) { Check $Label $false $probe.Error; return $null }
    $bodyOk = ($BodyMustContain -eq '') -or ($probe.Body -like "*$BodyMustContain*")
    Check $Label (($probe.Status -eq $ExpectedStatus) -and $bodyOk) "HTTP $($probe.Status)$(if (-not $bodyOk) { " (body lacks '$BodyMustContain')" })"
    return $probe
}

Write-Host ''
Write-Host "Sky LIS smoke test: $BaseUrl$(if ($ApiUrl) { " (API $ApiUrl)" })" -ForegroundColor Cyan
Write-Host ''

# --- through the site ----------------------------------------------------------------------------------------------
[void] (Test-Endpoint $BaseUrl '/health/live' 200 '"live"' 'site: /health/live')
[void] (Test-Endpoint $BaseUrl '/health/ready' 200 '"ready"' 'site: /health/ready (migrations applied, database reachable)')
$jwks = Test-Endpoint $BaseUrl '/.well-known/jwks.json' 200 '"keys"' 'site: /.well-known/jwks.json'
if ($jwks -and $jwks.Status -eq 200) {
    $ok = $false
    try { $document = $jwks.Body | ConvertFrom-Json; $ok = @($document.keys).Count -ge 1 -and $document.keys[0].kty -eq 'EC' -and $document.keys[0].crv -eq 'P-256' } catch { $ok = $false }
    Check 'site: JWKS publishes one P-256 (ES256) key' $ok ''
}

$index = Test-Endpoint $BaseUrl '/' 200 '<app-root>' 'site: / serves index.html'
if ($index -and $index.Status -eq 200) {
    Check 'site: index.html Cache-Control no-store' ((Get-Header $index.Headers 'Cache-Control') -like '*no-store*') (Get-Header $index.Headers 'Cache-Control')
    Check 'site: X-Content-Type-Options nosniff' ((Get-Header $index.Headers 'X-Content-Type-Options') -eq 'nosniff') ''
    Check 'site: X-Frame-Options DENY' ((Get-Header $index.Headers 'X-Frame-Options') -eq 'DENY') ''
    Check 'site: Content-Security-Policy present' ((Get-Header $index.Headers 'Content-Security-Policy') -like "*frame-ancestors 'none'*") ''
    $server = Get-Header $index.Headers 'Server'
    Check 'site: Server header removed' ($server -eq '') $server
    if ($BaseUrl.StartsWith('https://')) { Check 'site: Strict-Transport-Security' ((Get-Header $index.Headers 'Strict-Transport-Security') -like 'max-age=*') '' }

    if ($index.Body -match '(?i)<script[^>]*\bsrc="(main-[A-Za-z0-9_-]+\.js)"') {
        $bundle = Test-Endpoint $BaseUrl "/$($Matches[1])" 200 '' "site: hashed bundle $($Matches[1])"
        if ($bundle -and $bundle.Status -eq 200) { Check 'site: hashed bundle immutable' ((Get-Header $bundle.Headers 'Cache-Control') -like '*immutable*') (Get-Header $bundle.Headers 'Cache-Control') }
    } else {
        Check 'site: index.html references a main-*.js bundle' $false ''
    }
}
[void] (Test-Endpoint $BaseUrl '/treasury/access/smoke-probe' 200 '<app-root>' 'site: deep route falls back to index.html')
[void] (Test-Endpoint $BaseUrl '/i18n/auth.en.json' 200 '' 'site: i18n dictionary served')
$me = Test-Endpoint $BaseUrl '/api/v1/auth/me' 401 '' 'site: /api proxied and guarded (401 without token)'
if ($me -and $me.Status -eq 401) { Check 'site: API answers with its own no-store header' ((Get-Header $me.Headers 'Cache-Control') -eq 'no-store') (Get-Header $me.Headers 'Cache-Control') }

# --- directly on the loopback API -------------------------------------------------------------------------------------
if ($ApiUrl) {
    $saveHost = $HostHeader; $HostHeader = ''
    [void] (Test-Endpoint $ApiUrl '/health/live' 200 '"live"' 'api: /health/live')
    [void] (Test-Endpoint $ApiUrl '/health/ready' 200 '"ready"' 'api: /health/ready')
    [void] (Test-Endpoint $ApiUrl '/api/v1/auth/me' 401 '' 'api: /api/v1/auth/me is guarded')
    $HostHeader = $saveHost
}

Write-Host ''
if ($script:failures -eq 0) {
    Write-Host "All $script:passes checks passed." -ForegroundColor Green
    exit 0
}
Write-Host "$script:failures of $($script:passes + $script:failures) checks failed." -ForegroundColor Red
exit 1
