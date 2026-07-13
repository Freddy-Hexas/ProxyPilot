# ProxyPilot Test Plan

This plan verifies the current product goal:

```text
All normal traffic enters ProxyPilot first, then ProxyPilot decides DIRECT / PROXY / REJECT.
```

The plan covers system-proxy-aware apps, TUN-only apps, multi-process apps, rule changes, and port/connection monitoring.

## Test Tools

Use the monitor script in a separate PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File A:\ProxyPilot\scripts\monitor-traffic.ps1 -IntervalSeconds 2 -UpstreamPorts 20088
```

If SSR uses a different local port, replace `20088`. The script writes JSONL samples to `A:\ProxyPilot\artifacts\traffic-monitor-*.jsonl`.

Key ports:

```text
127.0.0.1:17990  ProxyPilot/mihomo mixed port, system proxy entry
127.0.0.1:19090  mihomo API
127.0.0.1:20088  current SSR upstream proxy on this machine
```

## Required Environment

1. Start SSR, but keep its local proxy service available.
2. Prefer disabling SSR's "auto set system proxy" so it does not overwrite ProxyPilot.
3. Run ProxyPilot as administrator when TUN is enabled.
4. Start ProxyPilot from:

```powershell
A:\ProxyPilot\artifacts\publish\ProxyPilot-Full-1.1.0-win-x64\ProxyPilot.exe
```

## Baseline Checks

### B1. Startup Takeover

Steps:

1. Start the monitor script.
2. Start ProxyPilot.
3. Wait 5 seconds.
4. Check Windows system proxy:

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' |
  Select-Object ProxyEnable,ProxyServer,AutoConfigURL
```

Expected:

```text
ProxyEnable = 1
ProxyServer = 127.0.0.1:17990
AutoConfigURL empty
```

Monitor expectation:

```text
Port 17990 is listening by mihomo.exe
Port 19090 is listening by mihomo.exe
ProxyPilot UI shows traffic first enters ProxyPilot
```

### B2. Upstream Detection

Steps:

1. Open `%LocalAppData%\ProxyPilot\config\config.process-manager.yaml`.
2. Confirm the upstream proxy points to SSR.

Expected:

```yaml
proxies:
- name: ProxyPilot Upstream
  type: http
  server: 127.0.0.1
  port: 20088
```

If the upstream is wrong, click "识别代理" or restart ProxyPilot while SSR is running.

### B3. Exit Restore

Steps:

1. Record the current system proxy while ProxyPilot is running.
2. Exit from tray menu, not just close the window.
3. Check Windows system proxy again.

Expected:

```text
The previous SSR/system proxy setting is restored, or proxy is disabled if it was disabled before ProxyPilot started.
```

## App Matrix

Use at least these programs:

```text
Chrome      multi-process, system proxy aware
Edge        multi-process, system proxy aware
Firefox     system proxy aware, independent network stack
PowerShell  Invoke-WebRequest, system proxy path
curl.exe    command-line path, may not always honor WinINET settings
WeChat      desktop app, often system proxy aware
BitComet or qBittorrent  TUN/non-system-proxy path
VS Code or Typora        multi-process desktop app
```

For each app, test three rule states:

```text
PROXY   should reach YouTube / Google through upstream SSR
DIRECT  should bypass upstream SSR; YouTube should fail in mainland China networks
REJECT  should fail quickly and should not create upstream traffic
```

Important: close existing tabs/connections or open a new private window after rule changes. Existing connections may survive a rule change.

## Browser Tests

### C1. Chrome PROXY

Steps:

1. Set `chrome.exe` to `PROXY`.
2. Click "应用规则".
3. Open a new Chrome incognito window.
4. Visit `https://www.youtube.com`.

Expected:

```text
YouTube opens.
Chrome connects to 127.0.0.1:17990.
mihomo connects to 127.0.0.1:20088.
Chrome must NOT connect directly to 127.0.0.1:20088.
```

Monitor evidence:

```text
ClientToProxyPilot: chrome.exe -> 127.0.0.1:17990
MihomoToUpstream: mihomo.exe -> 127.0.0.1:20088
```

### C2. Chrome DIRECT

Steps:

1. Set `chrome.exe` to `DIRECT`.
2. Click "应用规则".
3. Close existing YouTube tabs.
4. Open a new incognito window.
5. Visit `https://www.youtube.com`.

Expected:

```text
YouTube should fail on a mainland China network.
Chrome connects to 127.0.0.1:17990.
mihomo should NOT connect to 127.0.0.1:20088 for Chrome's new YouTube attempt.
```

Failure signal:

```text
If Chrome connects directly to 127.0.0.1:20088, SSR has reclaimed system proxy or Chrome has an explicit proxy setting.
```

### C3. Chrome REJECT

Steps:

1. Set `chrome.exe` to `REJECT`.
2. Click "应用规则".
3. Open a new incognito window.
4. Visit `https://www.youtube.com`.

Expected:

```text
The page fails quickly.
Chrome may connect to 127.0.0.1:17990.
mihomo should not create an upstream SSR connection for this request.
```

### C4. Edge and Firefox Repeat

Repeat C1-C3 for `msedge.exe` and `firefox.exe`.

Expected:

```text
Each browser obeys its own process rule.
Changing Chrome rule must not change Edge/Firefox behavior.
```

## Command-Line Tests

### P1. PowerShell Invoke-WebRequest

Steps:

1. Set `powershell.exe` or `pwsh.exe` to `PROXY`.
2. Run:

```powershell
Invoke-WebRequest https://www.youtube.com -UseBasicParsing -TimeoutSec 10
```

Expected:

```text
Request succeeds through 127.0.0.1:17990 and upstream 127.0.0.1:20088.
```

Then set `powershell.exe` / `pwsh.exe` to `DIRECT`.

Expected:

```text
Request should fail on a mainland China network.
No upstream SSR traffic for the new request.
```

### P2. curl.exe

Steps:

1. Test without explicit proxy:

```powershell
curl.exe -I https://www.youtube.com --max-time 10
```

2. Test explicit bypass attempt:

```powershell
curl.exe -x http://127.0.0.1:20088 -I https://www.youtube.com --max-time 10
```

Expected:

```text
Without explicit proxy: should be captured by system proxy or TUN.
With explicit -x 127.0.0.1:20088: this is a bypass/red-team case. Monitor whether curl connects directly to 20088.
```

If explicit `-x` bypasses ProxyPilot, record it as a hard limitation unless loopback interception/WFP is added.

## Desktop App Tests

### W1. WeChat DIRECT

Steps:

1. Set `Weixin.exe` to `DIRECT`.
2. Click "应用规则".
3. Send/receive a message or load a mini page.

Expected:

```text
WeChat new traffic enters 127.0.0.1:17990 if it honors system proxy.
No unnecessary 127.0.0.1:20088 upstream traffic for WeChat.
```

### W2. WeChat PROXY

Steps:

1. Set `Weixin.exe` to `PROXY`.
2. Click "应用规则".
3. Repeat network action.

Expected:

```text
WeChat traffic enters 17990, then mihomo may use 20088.
```

## TUN / Non-System-Proxy Tests

### T1. BitComet or qBittorrent PROXY

Steps:

1. Confirm ProxyPilot is running as administrator.
2. Set `BitComet.exe` or `qbittorrent.exe` to `PROXY`.
3. Start a small legal test torrent or tracker update.
4. Click "检测连接".

Expected:

```text
The app may not connect to 127.0.0.1:17990 because it may ignore system proxy.
Connections should appear in mihomo /connections via TUN.
ProxyPilot connection detection should show mihomo-managed connections.
```

Monitor evidence:

```text
mihomo /connections has process metadata for the torrent app.
If the app has many external connections and none appear in mihomo, TUN capture is not working.
```

### T2. BitComet or qBittorrent DIRECT

Expected:

```text
New torrent connections should bypass upstream SSR.
mihomo should not create 127.0.0.1:20088 upstream connections for that process.
```

### T3. BitComet or qBittorrent REJECT

Expected:

```text
New connections should fail or stop increasing.
No upstream SSR traffic should be created for that process.
```

## Multi-Process and Grouping Tests

### G1. Chrome Process Group

Steps:

1. Open multiple tabs in Chrome.
2. Refresh ProxyPilot.
3. Expand the Chrome group.

Expected:

```text
Chrome appears as one grouped app with multiple PIDs.
Rule change on the group applies to all chrome.exe child processes.
```

### G2. VS Code / Typora

Expected:

```text
Multi-process apps are grouped by process name.
Icon and app name are recognizable.
Sorting keeps high-memory apps near the top.
```

## Hot Reload Tests

### H1. Apply Without Restart

Steps:

1. Start monitor.
2. Set Chrome to `PROXY`, apply.
3. Set Chrome to `DIRECT`, apply.
4. Set Chrome to `REJECT`, apply.

Expected:

```text
API reload returns success each time.
mihomo process PID does not change.
`%LocalAppData%\ProxyPilot\config\config.process-manager.yaml` rules update each time.
New connections follow the new rule.
```

### H2. SAFE_PATHS Regression

Expected:

```text
No "path is not subpath of home directory or SAFE_PATHS" error.
If seen, restart mihomo from ProxyPilot and retest.
```

## Port Monitoring Rules

Use these interpretations while reading monitor output:

```text
chrome.exe -> 127.0.0.1:17990
  Good. Browser enters ProxyPilot first.

chrome.exe -> 127.0.0.1:20088
  Bad for this product goal. Browser bypasses ProxyPilot and enters SSR directly.

mihomo.exe -> 127.0.0.1:20088
  Good when the tested app rule is PROXY.
  Bad when the tested app rule is DIRECT or REJECT for a fresh request.

app.exe -> public IP directly
  For system-proxy-aware apps: suspicious.
  For TUN apps: verify with mihomo /connections before calling it a leak.

No 17990 traffic and no mihomo /connections
  The app is not being captured.
```

## Pass Criteria

ProxyPilot passes the intended behavior if:

```text
1. On startup, system proxy becomes 127.0.0.1:17990.
2. SSR's previous port is used only as ProxyPilot's upstream.
3. Chrome/Edge/Firefox do not connect directly to SSR's port.
4. PROXY creates mihomo -> upstream traffic.
5. DIRECT does not create upstream traffic for fresh connections.
6. REJECT blocks fresh connections without upstream traffic.
7. TUN apps appear in mihomo /connections when running as administrator.
8. Exiting ProxyPilot restores the previous system proxy.
9. CPU usage remains low while the window is backgrounded.
```

## Known Red-Team Cases

These are important to test because they define whether "all traffic" is truly enforced:

```text
App explicitly configured to 127.0.0.1:20088
App bundles its own VPN/TUN driver
App uses raw sockets or non-TCP/UDP protocols
App talks only to LAN/local addresses
SSR/Clash overwrites system proxy after ProxyPilot starts
Existing browser connections opened before rule change
```

If any red-team case bypasses ProxyPilot, record the exact process, port, and command line. Fixing those cases may require loopback interception, WFP, or app-specific configuration.
