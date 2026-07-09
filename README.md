# ProxyPilot

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-2563eb)
![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)
![Release](https://img.shields.io/badge/release-1.0.6-16a34a)
![License](https://img.shields.io/badge/license-MIT-111827)
⭐ If ProxyPilot helps you, please star the repository after it is published.

ProxyPilot is a Windows desktop tool for process-level network routing when you are already using a local proxy for scientific internet access.

Typical use case:

- Your browser needs `PROXY` because you use ChatGPT, Google, YouTube, GitHub, or other overseas services.
- WeChat, Baidu Netdisk, QQ, local games, or China-only apps do not need proxy traffic, so you set them to `DIRECT`.
- Some app should not access the network at all, so you set it to `REJECT`.

ProxyPilot does not build its own proxy protocol, does not inject DLLs, and does not install a custom WFP driver. It manages a bundled `mihomo.exe` and generates process rules for mihomo TUN mode.

## What It Does

ProxyPilot makes all supported traffic enter ProxyPilot first, then applies per-process rules:

```text
Chrome / browser -> ProxyPilot -> upstream proxy -> ChatGPT
WeChat           -> ProxyPilot -> DIRECT
Baidu Netdisk    -> ProxyPilot -> DIRECT
Blocked app      -> ProxyPilot -> REJECT
```

The upstream proxy can be an existing local proxy application such as:

- ShadowsocksR / SSR
- Clash
- Clash Verge / Clash Verge Rev
- v2rayN
- sing-box
- Other local HTTP/SOCKS proxy tools that expose a local port

ProxyPilot detects common local proxy ports automatically and checks whether the upstream proxy can actually reach Google / YouTube. This helps distinguish "ProxyPilot rule did not work" from "the upstream proxy itself is unavailable".

## Features

- Process list grouped by application, with icon, PID, path, and rule.
- Three rule actions:
  - `PROXY`: send the process through the upstream proxy group.
  - `DIRECT`: connect directly without the upstream proxy.
  - `REJECT`: block the process.
- Built-in `mihomo.exe`; users do not need to download mihomo separately.
- mihomo process start / stop / restart.
- mihomo API hot reload.
- TUN configuration generation.
- Upstream proxy detection and health check.
- TUN loopback risk detection.
- Connection inspection: total connections, mihomo-managed connections, upstream connections, and suspected direct connections.
- Tray mode.
- Chinese UI by default, with English switch.
- Self-contained Windows x64 release.

## Download

Go to the `release/` folder and download:

```text
ProxyPilot-1.0.6-win-x64.zip
```

Unzip it and run:

```text
ProxyPilot.exe
```

No separate .NET runtime installation is required. No separate mihomo download is required.

## Requirements

- Windows 10 or Windows 11
- Administrator permission for TUN mode
- An upstream proxy if you want `PROXY` traffic to reach overseas services

ProxyPilot itself is not a proxy service provider. It routes traffic to your existing upstream proxy.

## Quick Start

1. Start your existing proxy tool first, for example SSR or Clash.
2. Make sure your upstream proxy can normally access ChatGPT / Google / YouTube by itself.
3. Run `ProxyPilot.exe` as administrator.
4. ProxyPilot will start the bundled `mihomo.exe` automatically.
5. Click `Detect Proxy` / `识别代理` if the upstream proxy was not detected.
6. Click `Check Upstream` / `检测上游` to verify whether the upstream proxy can reach Google / YouTube.
7. In the process list:
   - Set your browser, for example `chrome.exe`, to `PROXY`.
   - Set `WeChat.exe`, Baidu Netdisk, or other China-only apps to `DIRECT`.
   - Set unwanted network apps to `REJECT`.
8. Click `Apply` / `应用规则`.
9. If Chrome has old connections, restart Chrome for the most reliable switch.

## Example Workflow

You are using SSR. The SSR local proxy port is `127.0.0.1:20088`.

You want:

```text
Chrome       -> PROXY  -> SSR 20088 -> ChatGPT
WeChat       -> DIRECT -> local direct connection
BaiduNetdisk -> DIRECT -> local direct connection
```

In ProxyPilot:

1. Confirm upstream shows something like `127.0.0.1:20088`.
2. Set `Google Chrome` to `PROXY`.
3. Set `WeChat` to `DIRECT`.
4. Set `Baidu Netdisk` to `DIRECT`.
5. Click `Apply`.

The chain should be:

```text
Chrome -> ProxyPilot -> SSR 20088
WeChat -> ProxyPilot -> DIRECT
```

## Meaning of Rules

### PROXY

`PROXY` means the process is routed to ProxyPilot's upstream proxy group.

It does not mean "guaranteed access to the internet". If SSR / Clash / your upstream node is broken, `PROXY` will also fail. Use the upstream health check to confirm the upstream proxy itself is available.

### DIRECT

`DIRECT` means ProxyPilot asks mihomo to connect directly, without the upstream proxy.

If another proxy tool is running in global mode outside ProxyPilot, that external tool may still affect traffic. For the cleanest result, let traffic enter ProxyPilot first and avoid competing global proxy/TUN tools.

### REJECT

`REJECT` blocks matching process traffic.

## Why Administrator Permission Is Needed

ProxyPilot uses mihomo TUN mode. TUN allows ProxyPilot to handle traffic from applications that ignore the Windows system proxy, such as download tools or some game launchers.

On Windows, TUN routing usually needs administrator permission.

## Upstream Health Check

ProxyPilot can detect a local proxy port, but detecting a port is not enough. A local port can be open while the proxy node is dead.

The upstream health check verifies whether the detected upstream port can access Google / YouTube through HTTP/SOCKS tunneling.

If the UI says upstream is unavailable, fix SSR / Clash / node subscription first.

## Chrome Hot Reload Notes

Chrome keeps connection pools, background Network Service connections, and QUIC / HTTP3 sessions.

After changing a Chrome rule:

- New connections should follow the new rule.
- Existing connections may not switch immediately.
- Restarting Chrome is the most reliable way to confirm a route change.

ProxyPilot includes a selected-process restart helper for this reason.

## Files Created at Runtime

ProxyPilot creates runtime files next to the executable:

```text
data/settings.json
data/user-rules.json
config/template.yaml
config/config.process-manager.yaml
config/mihomo-generated.yaml
```

These files are user-specific and should not be committed as source code.

## Build From Source

Install .NET 8 SDK, then run:

```powershell
dotnet restore .\ProcessProxyManager.slnx
dotnet build .\ProcessProxyManager.slnx -c Release
```

Create a self-contained release zip:

```powershell
.\scripts\package.ps1 -Version 1.0.6
```

The packaged build includes:

```text
ProxyPilot.exe
resources/mihomo.exe
resources/ProxyPilot.ico
```

## Project Structure

```text
ProcessProxyManager.App/      WPF desktop UI
ProcessProxyManager.Core/     rules, settings, JSON stores, process scanning
ProcessProxyManager.Mihomo/   mihomo config, process manager, API client
ProcessProxyManager.Native/   Windows proxy detection and connection inspection
resources/                    bundled mihomo.exe
scripts/                      packaging script
release/                      ready-to-download release zip
```

## Limitations

- ProxyPilot does not provide proxy nodes.
- ProxyPilot does not guarantee 100% no-leak networking.
- Some traffic may be local, LAN, system-level, or already established before a rule switch.
- Chrome and Chromium-based browsers may need restart after rule changes.
- Running multiple TUN/global proxy tools at the same time can create confusing routes.

## Star Record

This table can be updated after the project is published on GitHub.

| Date | Stars | Note |
| --- | ---: | --- |
| 2026-07-09 | 0 | Initial public-ready 1.0.6 package |

## License

MIT
