$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publish = Join-Path $root "publish"
$iss = Join-Path $root "installer\PickmeTurn.iss"

Write-Host "== PickmeTurn installer build ==" -ForegroundColor Cyan

& (Join-Path $root "build.ps1")

if (-not (Test-Path (Join-Path $publish "PickmeTurn.exe"))) {
    throw "publish\PickmeTurn.exe was not produced."
}

$isccPath = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"

if (-not (Test-Path $isccPath)) {
    throw "Inno Setup compiler not found: $isccPath"
}

if (-not $isccPath) {
    throw "Inno Setup is required. Install Inno Setup 6 or 7 and run this script again."
}

& $isccPath $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

Write-Host "" 
Write-Host "DONE:" -ForegroundColor Green
Write-Host (Join-Path $root "installer-output\PickmeTurn-Setup-1.2.2.exe")

