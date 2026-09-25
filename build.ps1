$ErrorActionPreference = "Stop"
Write-Host "== PickmeTurn 1.0.0 build ==" -ForegroundColor Cyan

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$assets = Join-Path $root "Assets"

if (-not (Test-Path (Join-Path $assets "client-windows-amd64.exe"))) {
  throw "Missing Assets\client-windows-amd64.exe"
}
if (-not (Test-Path (Join-Path $assets "wireguard.exe"))) {
  throw "Missing Assets\wireguard.exe"
}
if (-not (Test-Path (Join-Path $assets "wg.exe"))) {
  throw "Missing Assets\wg.exe"
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
  throw ".NET 8 SDK is required only on the BUILD machine. The resulting client is self-contained."
}

Write-Host "Publishing single-file Win-x64 application..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "PickmeTurn.csproj") `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o (Join-Path $root "publish")

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host ""
Write-Host "DONE:" -ForegroundColor Green
Write-Host (Join-Path $root "publish\PickmeTurn.exe")
