param(
    [int]$IntervalSeconds = 2,
    [int[]]$ProxyPilotPorts = @(17990, 19090),
    [int[]]$UpstreamPorts = @(20088, 7890, 7897, 1080, 10808, 10809),
    [string]$OutputPath = ""
)

$ErrorActionPreference = "SilentlyContinue"

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath = Join-Path $root "artifacts\traffic-monitor-$stamp.jsonl"
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

function Get-ProcessNameById {
    param([int]$ProcessId)

    try {
        return (Get-Process -Id $ProcessId -ErrorAction Stop).ProcessName
    }
    catch {
        return ""
    }
}

function Get-SystemProxySnapshot {
    try {
        $settings = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
        return [pscustomobject]@{
            proxyEnable = [int]$settings.ProxyEnable
            proxyServer = [string]$settings.ProxyServer
            autoConfigUrl = [string]$settings.AutoConfigURL
        }
    }
    catch {
        return [pscustomobject]@{
            proxyEnable = $null
            proxyServer = ""
            autoConfigUrl = ""
        }
    }
}

function Get-WatchedConnections {
    $watchPorts = @($ProxyPilotPorts + $UpstreamPorts | Select-Object -Unique)

    Get-NetTCPConnection -ErrorAction SilentlyContinue |
        Where-Object {
            $_.LocalPort -in $watchPorts -or
            $_.RemotePort -in $watchPorts -or
            $_.OwningProcess -in @((Get-Process mihomo, ProxyPilot, chrome, msedge, firefox, Weixin, BitComet, qbittorrent, powershell, pwsh, curl, ShadowsocksR-dotnet4.0 -ErrorAction SilentlyContinue).Id)
        } |
        Select-Object State, LocalAddress, LocalPort, RemoteAddress, RemotePort, OwningProcess,
            @{ Name = "ProcessName"; Expression = { Get-ProcessNameById $_.OwningProcess } }
}

Write-Host "ProxyPilot traffic monitor"
Write-Host "Output: $OutputPath"
Write-Host "ProxyPilot ports: $($ProxyPilotPorts -join ', ')"
Write-Host "Upstream ports: $($UpstreamPorts -join ', ')"
Write-Host "Press Ctrl+C to stop."

while ($true) {
    $connections = @(Get-WatchedConnections)
    $portSummary = $connections |
        Group-Object RemotePort, ProcessName |
        Sort-Object Count -Descending |
        Select-Object -First 20 Count, Name

    $sample = [pscustomobject]@{
        timestamp = (Get-Date).ToString("o")
        systemProxy = Get-SystemProxySnapshot
        processCount = $connections.Count
        connections = $connections
    }

    $sample | ConvertTo-Json -Depth 6 -Compress | Add-Content -LiteralPath $OutputPath

    Clear-Host
    Write-Host "ProxyPilot traffic monitor  $(Get-Date -Format 'HH:mm:ss')"
    Write-Host "System proxy:"
    Get-SystemProxySnapshot | Format-List
    Write-Host "Port summary:"
    $portSummary | Format-Table -AutoSize
    Write-Host "Recent watched connections:"
    $connections |
        Sort-Object ProcessName, RemotePort, State |
        Select-Object -First 40 ProcessName, OwningProcess, State, LocalAddress, LocalPort, RemoteAddress, RemotePort |
        Format-Table -AutoSize
    Write-Host "Writing JSONL: $OutputPath"

    Start-Sleep -Seconds $IntervalSeconds
}
