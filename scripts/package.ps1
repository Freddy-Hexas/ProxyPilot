param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish\ProxyPilot-$Version-$Runtime"
$zipPath = Join-Path $root "artifacts\ProxyPilot-$Version-$Runtime.zip"

Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue

$projectPath = Join-Path $root "ProcessProxyManager.App\ProcessProxyManager.App.csproj"

dotnet restore $projectPath -r $Runtime --force

if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --no-restore `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $publishDir "ProxyPilot.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "dotnet publish did not produce ProxyPilot.exe"
}

New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "resources") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "config") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "data") | Out-Null

if (Test-Path (Join-Path $root "resources\mihomo.exe")) {
    Copy-Item -LiteralPath (Join-Path $root "resources\mihomo.exe") -Destination (Join-Path $publishDir "resources\mihomo.exe") -Force
}

Copy-Item -LiteralPath (Join-Path $root "ProcessProxyManager.App\Assets\ProxyPilot.ico") -Destination (Join-Path $publishDir "resources\ProxyPilot.ico") -Force

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host "Created $zipPath"
