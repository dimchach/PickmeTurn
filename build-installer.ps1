$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publish = Join-Path $root "publish"
$iss = Join-Path $root "installer\PickmeTurn.iss"

Write-Host "== PickmeTurn installer build ==" -ForegroundColor Cyan

& (Join-Path $root "build.ps1")

if (-not (Test-Path (Join-Path $publish "PickmeTurn.exe"))) {
    throw "publish\PickmeTurn.exe was not produced."
}

$isccPath = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $isccPath) {
    $candidates = @(
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }
    if ($candidates.Count -gt 0) { $isccPath = $candidates[0] }
}

if (-not $isccPath) {
    throw "Inno Setup is required. Install Inno Setup 6 or 7 and run this script again."
}

& $isccPath $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

Write-Host "" 
Write-Host "DONE:" -ForegroundColor Green
Write-Host (Join-Path $root "installer-output\PickmeTurn-Setup-1.0.0.exe")
