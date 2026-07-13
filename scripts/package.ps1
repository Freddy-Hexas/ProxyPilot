param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.1.0",
    [ValidateSet("All", "Lite", "Full")]
    [string]$Edition = "All"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root "ProcessProxyManager.App\ProcessProxyManager.App.csproj"
$artifactsRoot = Join-Path $root "artifacts"

function Get-FriendlySize([long]$Bytes) {
    if ($Bytes -ge 1GB) { return "{0:N2} GB" -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return "{0:N2} MB" -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return "{0:N2} KB" -f ($Bytes / 1KB) }
    return "$Bytes bytes"
}

function Publish-Edition(
    [string]$Name,
    [bool]$SelfContained
) {
    $artifactName = "ProxyPilot-$Name-$Version-$Runtime"
    $publishDir = Join-Path $artifactsRoot "publish\$artifactName"
    $zipPath = Join-Path $artifactsRoot "$artifactName.zip"
    $resolvedPublishDir = [System.IO.Path]::GetFullPath($publishDir)
    $resolvedArtifactsRoot = [System.IO.Path]::GetFullPath($artifactsRoot)
    if (-not $resolvedPublishDir.StartsWith($resolvedArtifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove publish directory outside artifacts: $resolvedPublishDir"
    }

    Remove-Item -LiteralPath $resolvedPublishDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue

    $arguments = @(
        "publish",
        $projectPath,
        "-c", $Configuration,
        "--self-contained", $SelfContained.ToString().ToLowerInvariant(),
        "-p:DebugType=none",
        "-p:DebugSymbols=false",
        "-o", $resolvedPublishDir
    )

    if ($SelfContained) {
        $arguments += @("-r", $Runtime)
        $arguments += "-p:PublishSingleFile=true"
        $arguments += "-p:IncludeNativeLibrariesForSelfExtract=true"
    }
    else {
        $arguments += "-p:PublishSingleFile=false"
        $arguments += "-p:UseAppHost=true"
    }

    & dotnet @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Name with exit code $LASTEXITCODE"
    }

    $exePath = Join-Path $resolvedPublishDir "ProxyPilot.exe"
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "dotnet publish did not produce ProxyPilot.exe for $Name"
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $resolvedPublishDir "resources") | Out-Null
    if (Test-Path (Join-Path $root "resources\mihomo.exe")) {
        Copy-Item -LiteralPath (Join-Path $root "resources\mihomo.exe") `
            -Destination (Join-Path $resolvedPublishDir "resources\mihomo.exe") -Force
    }

    Copy-Item -LiteralPath (Join-Path $root "ProcessProxyManager.App\Assets\ProxyPilot.ico") `
        -Destination (Join-Path $resolvedPublishDir "resources\ProxyPilot.ico") -Force

    Compress-Archive -Path (Join-Path $resolvedPublishDir "*") -DestinationPath $zipPath -Force

    $expandedBytes = (Get-ChildItem -LiteralPath $resolvedPublishDir -File -Recurse |
        Measure-Object -Property Length -Sum).Sum
    $zipBytes = (Get-Item -LiteralPath $zipPath).Length

    [pscustomobject]@{
        Edition = $Name
        ExpandedBytes = $expandedBytes
        ExpandedSize = Get-FriendlySize $expandedBytes
        ZipBytes = $zipBytes
        ZipSize = Get-FriendlySize $zipBytes
        ZipPath = $zipPath
    }
}

$results = @()
if ($Edition -in @("All", "Lite")) {
    $results += Publish-Edition -Name "Lite" -SelfContained $false
}
if ($Edition -in @("All", "Full")) {
    $results += Publish-Edition -Name "Full" -SelfContained $true
}

$results | Format-Table Edition, ExpandedSize, ZipSize, ZipPath -AutoSize
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactsRoot "package-sizes.json") -Encoding UTF8
