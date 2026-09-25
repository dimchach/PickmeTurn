$ErrorActionPreference = "Stop"
Write-Host "== PickmeTurn 1.1.0 build ==" -ForegroundColor Cyan

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$assets = Join-Path $root "Assets"
$freeTurnVersion = "4.0.1"
$freeTurnAsset = "client-windows-amd64.exe"
$freeTurnUrl = "https://github.com/samosvalishe/free-turn-proxy/releases/download/v$freeTurnVersion/$freeTurnAsset"
$checksumsUrl = "https://github.com/samosvalishe/free-turn-proxy/releases/download/v$freeTurnVersion/checksums.txt"

New-Item -ItemType Directory -Force $assets | Out-Null

function Ensure-FreeTurnCore {
  $target = Join-Path $assets $freeTurnAsset
  $tmp = Join-Path $env:TEMP "PickmeTurn-$freeTurnVersion-$freeTurnAsset"
  $checksumFile = Join-Path $env:TEMP "PickmeTurn-$freeTurnVersion-checksums.txt"

  Write-Host "Downloading FreeTurn core v$freeTurnVersion..." -ForegroundColor Cyan
  Invoke-WebRequest -Uri $freeTurnUrl -OutFile $tmp
  Invoke-WebRequest -Uri $checksumsUrl -OutFile $checksumFile

  $expected = $null
  foreach ($line in Get-Content $checksumFile) {
    if ($line -match '^([0-9a-fA-F]{64})\s+\*?(.+)$' -and [IO.Path]::GetFileName($Matches[2]) -eq $freeTurnAsset) {
      $expected = $Matches[1].ToLowerInvariant()
      break
    }
  }

  if (-not $expected) {
    throw "Could not find SHA-256 for $freeTurnAsset in $checksumsUrl"
  }

  $actual = (Get-FileHash -Algorithm SHA256 -Path $tmp).Hash.ToLowerInvariant()
  if ($actual -ne $expected) {
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    throw "FreeTurn SHA-256 mismatch. Expected $expected, got $actual"
  }

  Move-Item -Force $tmp $target
  Remove-Item $checksumFile -Force -ErrorAction SilentlyContinue
  Write-Host "FreeTurn core v$freeTurnVersion verified: $actual" -ForegroundColor Green
}

Ensure-FreeTurnCore

foreach ($name in @("wireguard.exe", "wg.exe")) {
  if (-not (Test-Path (Join-Path $assets $name))) {
    throw "Missing Assets\$name"
  }
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
