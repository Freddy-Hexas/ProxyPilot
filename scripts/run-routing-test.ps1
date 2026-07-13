param(
    [string]$PublishDir = "A:\ProxyPilot\artifacts\publish\ProxyPilot-Full-1.1.0-win-x64",
    [string]$ApiUrl = "http://127.0.0.1:19090",
    [string]$Secret = "",
    [string]$TargetUrl = "https://speed.cloudflare.com/__down?bytes=20000000"
)

$ErrorActionPreference = "Stop"

$stateRoot = Join-Path $env:LOCALAPPDATA "ProxyPilot"
$configDir = Join-Path $stateRoot "config"
$settingsPath = Join-Path $stateRoot "data\settings.json"
$originalConfigPath = Join-Path $configDir "config.process-manager.yaml"
$artifactsDir = Join-Path (Split-Path -Parent $PSScriptRoot) "artifacts"
$testExePath = Join-Path $artifactsDir "pp-route-probe.exe"
$testProcessName = Split-Path -Leaf $testExePath
$resultPath = Join-Path $artifactsDir ("routing-test-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".json")

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($Secret)) {
    $Secret = [string]$settings.secret
}
$upstreamHost = if ([string]::IsNullOrWhiteSpace($settings.upstreamProxyHost)) { "127.0.0.1" } else { [string]$settings.upstreamProxyHost }
$upstreamPort = [int]$settings.upstreamProxyPort
$upstreamType = if ([string]::IsNullOrWhiteSpace($settings.upstreamProxyType)) { "http" } else { [string]$settings.upstreamProxyType }

$curl = (Get-Command curl.exe -ErrorAction Stop).Source
Copy-Item -LiteralPath $curl -Destination $testExePath -Force

function Invoke-MihomoApi {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null
    )

    $headers = @{ Authorization = "Bearer $Secret" }
    $uri = "$ApiUrl$Path"

    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -TimeoutSec 5
    }

    $json = $Body | ConvertTo-Json -Compress
    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body $json -TimeoutSec 5
}

function Write-TestConfig {
    param([string]$Action)

    $path = Join-Path $configDir "routing-test-$($Action.ToLowerInvariant()).yaml"
    $yaml = @"
mixed-port: 17990
allow-lan: false
find-process-mode: always
mode: rule
log-level: info
external-controller: 127.0.0.1:19090
secret: $Secret
proxies:
- name: ProxyPilot Upstream
  type: $upstreamType
  server: $upstreamHost
  port: $upstreamPort
proxy-groups:
- name: PROXY
  type: select
  proxies:
  - ProxyPilot Upstream
  - DIRECT
rules:
- PROCESS-NAME,$testProcessName,$Action
- MATCH,DIRECT
tun:
  enable: true
  stack: mixed
  dns-hijack:
  - any:53
  auto-route: true
  auto-detect-interface: true
dns:
  enable: true
  enhanced-mode: fake-ip
  nameserver:
  - 223.5.5.5
  - 8.8.8.8
"@

    Set-Content -LiteralPath $path -Value $yaml -Encoding UTF8
    return $path
}

function Reload-Config {
    param([string]$Path)

    try {
        Invoke-MihomoApi -Method Put -Path "/configs" -Body @{ path = (Resolve-Path $Path).Path } | Out-Null
        return @{ success = $true; message = "ok" }
    }
    catch {
        return @{ success = $false; message = $_.Exception.Message }
    }
}

function Get-SystemProxy {
    $proxy = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
    return [ordered]@{
        proxyEnable = [int]$proxy.ProxyEnable
        proxyServer = [string]$proxy.ProxyServer
        autoConfigUrl = [string]$proxy.AutoConfigURL
    }
}

function Get-ListeningPorts {
    Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -in 17990, 19090, $upstreamPort } |
        Select-Object LocalAddress, LocalPort, OwningProcess,
            @{ Name = "ProcessName"; Expression = { (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName } }
}

function Get-TestConnections {
    param([int]$TestProcessId)

    $mihomoIds = @((Get-Process mihomo -ErrorAction SilentlyContinue).Id)

    Get-NetTCPConnection -ErrorAction SilentlyContinue |
        Where-Object {
            ($_.OwningProcess -eq $TestProcessId -and ($_.RemotePort -in 17990, $upstreamPort -or $_.LocalPort -in 17990, $upstreamPort)) -or
            ($_.OwningProcess -in $mihomoIds -and $_.RemotePort -eq $upstreamPort)
        } |
        Select-Object State, LocalAddress, LocalPort, RemoteAddress, RemotePort, OwningProcess,
            @{ Name = "ProcessName"; Expression = { (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName } }
}

function Read-TextFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    return [System.IO.File]::ReadAllText($Path)
}

function Get-MihomoProcessConnections {
    $response = Invoke-MihomoApi -Method Get -Path "/connections"
    @($response.connections) |
        Where-Object { $_.metadata.process -ieq $testProcessName } |
        Select-Object @{ Name = "process"; Expression = { $_.metadata.process } },
            @{ Name = "network"; Expression = { $_.metadata.network } },
            @{ Name = "host"; Expression = { $_.metadata.host } },
            @{ Name = "destinationIP"; Expression = { $_.metadata.destinationIP } },
            @{ Name = "destinationPort"; Expression = { $_.metadata.destinationPort } },
            chains
}

function Invoke-RouteCase {
    param([string]$Action)

    $configPath = Write-TestConfig -Action $Action
    $reload = Reload-Config -Path $configPath
    if (-not $reload.success) {
        return [ordered]@{
            action = $Action
            reload = $reload
            skipped = $true
            reason = "config reload failed"
        }
    }

    $stdout = Join-Path $artifactsDir "pp-route-probe-$Action.out"
    $stderr = Join-Path $artifactsDir "pp-route-probe-$Action.err"
    Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue

    $arguments = @(
        "-x", "http://127.0.0.1:17990",
        "-L",
        "--limit-rate", "40k",
        "--max-time", "10",
        "-o", "NUL",
        "-sS",
        $TargetUrl
    )

    $process = Start-Process -FilePath $testExePath -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    Start-Sleep -Seconds 2

    $tcpSnapshot = @(Get-TestConnections -TestProcessId $process.Id)
    $mihomoSnapshot = @(Get-MihomoProcessConnections)

    if (-not $process.WaitForExit(12000)) {
        Stop-Process -Id $process.Id -Force
        $timedOut = $true
    }
    else {
        $timedOut = $false
    }

    $exitCode = $null
    try { $exitCode = $process.ExitCode } catch {}

    return [ordered]@{
        action = $Action
        reload = $reload
        processId = $process.Id
        exitCode = $exitCode
        timedOutAndKilled = $timedOut
        tcpSnapshot = $tcpSnapshot
        mihomoConnections = $mihomoSnapshot
        stdout = Read-TextFile -Path $stdout
        stderr = Read-TextFile -Path $stderr
    }
}

$summary = [ordered]@{
    timestamp = (Get-Date).ToString("o")
    publishDir = $PublishDir
    isAdministratorShell = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    systemProxyBefore = Get-SystemProxy
    listeningPortsBefore = @(Get-ListeningPorts)
    apiVersion = $null
    cases = @()
    restore = $null
    resultPath = $resultPath
}

try {
    $summary.apiVersion = Invoke-MihomoApi -Method Get -Path "/version"
    foreach ($action in @("PROXY", "DIRECT", "REJECT")) {
        $summary.cases += Invoke-RouteCase -Action $action
    }
}
finally {
    if (Test-Path -LiteralPath $originalConfigPath) {
        $summary.restore = Reload-Config -Path $originalConfigPath
    }

    $summary.systemProxyAfter = Get-SystemProxy
    $summary.listeningPortsAfter = @(Get-ListeningPorts)
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8
}

$summary | ConvertTo-Json -Depth 8
