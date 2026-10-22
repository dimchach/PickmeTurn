$ErrorActionPreference = "Stop"
Write-Host "== PickmeTurn 1.2.6 + FreeTurn 4.1.0 Advanced CAPTCHA build ==" -ForegroundColor Cyan

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$assets = Join-Path $root "Assets"
$coreVersion = "4.1.0"
$coreArchive = Join-Path $env:TEMP "free-turn-proxy-v$coreVersion.zip"
$coreExtract = Join-Path $env:TEMP "free-turn-proxy-v$coreVersion-PickmeTurn"
$coreUrl = "https://github.com/hackdiaz-dev/free-turn-proxy/archive/refs/tags/v$coreVersion.zip"
New-Item -ItemType Directory -Force $assets | Out-Null

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw ".NET 8 SDK is required. Install it and rerun build.ps1." }

# Use a system Go installation if present. Otherwise download the official portable
# Go toolchain to TEMP; nothing is installed system-wide and PATH is not modified.
$goCommand = Get-Command go -ErrorAction SilentlyContinue
if ($goCommand) {
  $goExe = $goCommand.Source
  Write-Host "Using installed Go: $(& $goExe version)" -ForegroundColor Green
} else {
  $goVersion = "1.26.10"
  $goZip = Join-Path $env:TEMP "go$goVersion.windows-amd64.zip"
  $goRoot = Join-Path $env:TEMP "PickmeTurn-go-$goVersion"
  $goUrl = "https://go.dev/dl/go$goVersion.windows-amd64.zip"
  $goSha256 = "7ECD2D7260E82C59BCBD7F1077A07CEE2DEA607295B858DF67195A1E4E09629B"

  if (-not (Test-Path (Join-Path $goRoot "go\bin\go.exe"))) {
    Write-Host "Go was not found. Downloading official portable Go $goVersion (about 72 MB)..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $goUrl -OutFile $goZip
    $actualGoHash = (Get-FileHash -Algorithm SHA256 -Path $goZip).Hash.ToUpperInvariant()
    if ($actualGoHash -ne $goSha256) {
      Remove-Item $goZip -Force -ErrorAction SilentlyContinue
      throw "Go archive SHA-256 mismatch. Download was rejected."
    }
    if (Test-Path $goRoot) { Remove-Item $goRoot -Recurse -Force }
    New-Item -ItemType Directory -Force $goRoot | Out-Null
    Expand-Archive -Path $goZip -DestinationPath $goRoot -Force
  }
  $goExe = Join-Path $goRoot "go\bin\go.exe"
  if (-not (Test-Path $goExe)) { throw "Portable Go download/extraction failed: $goExe was not found." }
  Write-Host "Using temporary Go: $(& $goExe version)" -ForegroundColor Green
}

Write-Host "Downloading FreeTurn source v$coreVersion..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $coreUrl -OutFile $coreArchive
if (Test-Path $coreExtract) { Remove-Item $coreExtract -Recurse -Force }
New-Item -ItemType Directory -Force $coreExtract | Out-Null
Expand-Archive -Path $coreArchive -DestinationPath $coreExtract -Force
$repo = Get-ChildItem $coreExtract -Directory | Select-Object -First 1
if (-not $repo) { throw "Could not locate extracted FreeTurn source." }

# Add a real CLI flag to upstream v4.1.0 so the UI toggle controls the existing AdvancedCaptcha config.
$flagsPath = Join-Path $repo.FullName "internal\config\cliflags.go"
if (-not (Test-Path $flagsPath)) { throw "FreeTurn source layout changed: internal\config\cliflags.go was not found." }
$flags = Get-Content $flagsPath -Raw
# Match the source line by its ASCII flag identifier so this script is safe in
# Windows PowerShell 5 regardless of the console's legacy code page.
$manualFlagMatch = [regex]::Match($flags, '(?m)^\s*fs\.BoolVar\(&r\.ManualCaptcha,.*$')
if ($flags.Contains('"advanced-captcha"')) {
  Write-Host "Advanced CAPTCHA CLI flag already present." -ForegroundColor Yellow
} elseif (-not $manualFlagMatch.Success) {
  throw "Could not patch FreeTurn CLI: expected manual-captcha flag was not found. No core binary was produced."
} else {
  $manualFlagLine = $manualFlagMatch.Value
  $advancedFlagLine = '`tfs.BoolVar(&r.AdvancedCaptcha, "advanced-captcha", r.AdvancedCaptcha, "enable advanced automatic CAPTCHA solving; only with -provider vk")'
  $replacement = $manualFlagLine + "`r`n" + $advancedFlagLine.Replace('`t', "`t")
  $flags = $flags.Substring(0, $manualFlagMatch.Index) + $replacement + $flags.Substring($manualFlagMatch.Index + $manualFlagMatch.Length)
  Set-Content -Path $flagsPath -Value $flags -Encoding utf8
}

Write-Host "Building patched FreeTurn core..." -ForegroundColor Cyan
Push-Location $repo.FullName
try {
  & $goExe build -trimpath -ldflags "-s -w -X main.version=4.1.0-pickmeturn" -o (Join-Path $assets "client-windows-amd64.exe") ./cmd/client
  if ($LASTEXITCODE -ne 0) { throw "FreeTurn core build failed (exit code $LASTEXITCODE)." }
} finally { Pop-Location }

$expectedHashes = @{
  "wireguard.exe" = "04C71FB1A555A6ACE40D3AE9113C90C4829D7CA1D6C903FD20A8D41A0E2BEF94"
  "wg.exe"        = "E1F8CB5C9E30A878FA7EB0B2251BB4EB4946E669063E6FD5E4383CA4BD0DCAF2"
}
foreach ($name in @("wireguard.exe", "wg.exe")) {
  $file = Join-Path $assets $name
  if (-not (Test-Path $file)) { throw "Missing Assets\$name" }
  $actual = (Get-FileHash -Algorithm SHA256 -Path $file).Hash.ToUpperInvariant()
  if ($actual -ne $expectedHashes[$name]) { throw "$name SHA-256 mismatch. Expected $($expectedHashes[$name]), got $actual" }
  Write-Host "$name SHA-256 verified: $actual" -ForegroundColor Green
}

Write-Host "Publishing single-file Win-x64 application..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "PickmeTurn.csproj") `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o (Join-Path $root "publish")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
Write-Host "`nDONE:" -ForegroundColor Green
Write-Host (Join-Path $root "publish\PickmeTurn.exe")
