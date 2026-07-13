# ProxyPilot

ProxyPilot is a Windows process rule manager for mihomo/Clash Meta. It lets you choose a running process and set it to `PROXY`, `DIRECT`, or `REJECT`, then writes mihomo-compatible `PROCESS-NAME` rules.

## Scope

- WPF app on .NET 10
- Running process list with PID, path, current rule, quick action buttons, and connection counters
- Rule persistence in `%LocalAppData%\ProxyPilot\data\user-rules.json`
- mihomo rule snippet generation in `%LocalAppData%\ProxyPilot\config\mihomo-generated.yaml`
- Full generated mihomo config in `%LocalAppData%\ProxyPilot\config\config.process-manager.yaml`
- Optional TUN/DNS block generation
- mihomo API health check and `/configs` hot reload
- child-process start/stop/restart for `resources/mihomo.exe`
- bundled `resources/mihomo.exe` in the packaged build
- local upstream proxy detection for Windows system proxy, Clash/Clash Verge/mihomo configs, common Clash and Shadowsocks/SSR ports
- conservative connection inspection with "suspected direct" counts
- tray mode with open/apply/start/stop/exit
- Chinese UI by default, with a button to switch between Chinese and English
- app/window/tray icon generated from the supplied PNG icon assets
- Lite and Full zip packaging

## Run

```powershell
dotnet run --project .\ProcessProxyManager.App\ProcessProxyManager.App.csproj
```

## Files

The app creates these files under `%LocalAppData%\ProxyPilot`:

```text
data/user-rules.json
data/settings.json
config/mihomo-generated.yaml
config/template.yaml
config/config.process-manager.yaml
```

Put `mihomo.exe` here when using the built-in start/stop controls:

```text
resources/mihomo.exe
```

## Package

```powershell
.\scripts\package.ps1 -Version 1.1.0
```

This writes framework-dependent `ProxyPilot-Lite-*` and self-contained
`ProxyPilot-Full-*` archives. Artifact sizes are recorded in
`artifacts/package-sizes.json`.

TUN usually requires administrator rights. ProxyPilot detects admin status and shows a warning state, but it does not force elevation.

## Test

The detailed manual test plan is in `docs/TEST_PLAN.md`.

Traffic monitor:

```powershell
.\scripts\monitor-traffic.ps1 -IntervalSeconds 2 -UpstreamPorts 20088
```
