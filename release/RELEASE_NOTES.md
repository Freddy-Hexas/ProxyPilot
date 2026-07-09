# ProxyPilot 1.0.6

## Download

Download `ProxyPilot-1.0.6-win-x64.zip` from this folder, unzip it, and run `ProxyPilot.exe`.

`mihomo.exe` is already bundled under `resources/`, so users do not need to download mihomo separately.

## Highlights

- Process-level `PROXY`, `DIRECT`, and `REJECT` rules.
- Works with an existing local proxy such as SSR, Clash, Clash Verge, v2rayN, or sing-box.
- Built-in mihomo TUN mode so applications that ignore the Windows system proxy can still be handled.
- Automatic local upstream proxy detection and health check.
- Chinese UI by default, with English switch.
- Self-contained Windows x64 build.

## Notes

- TUN requires administrator permission.
- `PROXY` means traffic is sent to the detected upstream proxy. If the upstream proxy itself is broken, `PROXY` will not magically make the network reachable.
- Chrome may keep old TCP/QUIC connections after hot reload. Restart Chrome if route switching appears delayed.
