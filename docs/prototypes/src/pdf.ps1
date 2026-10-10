# Prints an HTML document to PDF with headless Microsoft Edge (or Chrome), honouring the document's @media print CSS.
#   powershell -File docs/prototypes/src/pdf.ps1 -Html "E:\LIS\docs\prototypes\Sky-LIS_Prototype_Comparison.html"
param(
  [Parameter(Mandatory = $true)][string]$Html,
  [string]$Pdf = ""
)
$cands = @(
  "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
  "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
  "C:\Program Files\Google\Chrome\Application\chrome.exe",
  "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe")
$exe = $cands | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) { Write-Error "No Edge or Chrome installation found"; exit 1 }
$Html = (Resolve-Path $Html).Path
if (-not $Pdf) { $Pdf = [System.IO.Path]::ChangeExtension($Html, ".pdf") }
$profile = Join-Path $env:TEMP "skylis-pdf-profile"
New-Item -ItemType Directory -Force $profile | Out-Null
$url = "file:///" + ($Html -replace '\\', '/')
& $exe --headless=new --disable-gpu --no-first-run --no-default-browser-check "--user-data-dir=$profile" --print-to-pdf-no-header "--print-to-pdf=$Pdf" $url 2>$null | Out-Null
if (Test-Path $Pdf) { Write-Output ("PDF written: {0} ({1:N0} bytes)" -f $Pdf, (Get-Item $Pdf).Length) } else { Write-Error "PDF was not created"; exit 1 }
