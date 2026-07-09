# ProxyPilot

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-2563eb)
![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)
![Release](https://img.shields.io/badge/release-1.0.6-16a34a)
![License](https://img.shields.io/badge/license-MIT-111827)

如果 ProxyPilot 对你有帮助，欢迎给这个项目点一个 Star。

ProxyPilot 是一个 Windows 桌面工具，用来在你已经开启“梯子”或本机代理的情况下，按进程控制网络分流。

最典型的场景是：

- 浏览器需要走 `PROXY`，因为你要访问 ChatGPT、Google、YouTube、GitHub 等服务。
- 微信、百度网盘、QQ、国内游戏或国内软件不需要走梯子，所以设置为 `DIRECT` 直连。
- 某些软件不希望联网，可以设置为 `REJECT` 阻断。

ProxyPilot 不提供代理节点，不自研代理内核，不做 DLL 注入，也不安装自定义 WFP 驱动。它通过内置的 `mihomo.exe` 和 mihomo TUN + process 规则来实现按进程分流。

## 核心链路

ProxyPilot 的目标是让流量先进入 ProxyPilot，再按进程规则决定走向：

```text
Chrome / 浏览器 -> ProxyPilot -> 上游代理 -> ChatGPT
微信             -> ProxyPilot -> DIRECT
百度网盘         -> ProxyPilot -> DIRECT
需要阻断的软件   -> ProxyPilot -> REJECT
```

上游代理可以是你本机已经运行的代理工具，例如：

- ShadowsocksR / SSR
- Clash
- Clash Verge / Clash Verge Rev
- v2rayN
- sing-box
- 其它提供本地 HTTP / SOCKS 端口的代理工具

ProxyPilot 会自动识别常见本地代理端口，并检测这个上游代理端口是否真的能访问 Google / YouTube，避免用户误以为“规则没生效”，实际却是“上游代理本身不可用”。

## 功能特性

- 进程列表按应用聚合，显示图标、PID、路径、规则和连接信息。
- 三种规则：
  - `PROXY`：通过上游代理组。
  - `DIRECT`：不经过上游代理，直接连接。
  - `REJECT`：阻断该进程流量。
- 内置 `mihomo.exe`，用户不需要单独下载 mihomo。
- 支持启动 / 停止 / 重启 mihomo。
- 支持 mihomo API 热重载。
- 自动生成 TUN 配置。
- 自动识别本机上游代理。
- 上游代理健康检查。
- TUN 回环风险检测。
- 连接检测：当前连接数、mihomo 接管数、上游连接数、疑似直连数。
- 托盘常驻。
- 默认中文界面，可切换英文。
- Windows x64 self-contained 打包。

## 下载

进入 `release/` 文件夹，下载：

```text
ProxyPilot-1.0.6-win-x64.zip
```

解压后运行：

```text
ProxyPilot.exe
```

不需要单独安装 .NET 运行时。
不需要单独下载 mihomo。
压缩包里已经包含：

```text
ProxyPilot.exe
resources/mihomo.exe
resources/ProxyPilot.ico
```

## 系统要求

- Windows 10 或 Windows 11
- 使用 TUN 时需要管理员权限
- 如果希望 `PROXY` 能访问外网，需要你自己已有可用的上游代理

ProxyPilot 不是机场客户端，也不提供代理节点。它负责把不同进程分流到你已有的本机代理或直连。

## 快速开始

1. 先启动你已有的代理工具，例如 SSR 或 Clash。
2. 确认这个代理工具本身可以访问 ChatGPT / Google / YouTube。
3. 以管理员身份运行 `ProxyPilot.exe`。
4. ProxyPilot 会自动启动内置的 `mihomo.exe`。
5. 如果没有识别到上游代理，点击 `识别代理`。
6. 点击 `检测上游`，确认上游代理是否可用。
7. 在进程列表里设置规则：
   - 浏览器，例如 `chrome.exe`，设置为 `PROXY`。
   - 微信、百度网盘等国内软件设置为 `DIRECT`。
   - 不希望联网的软件设置为 `REJECT`。
8. 点击 `应用规则`。
9. 如果 Chrome 仍然沿用旧连接，重启 Chrome 后再测试，结果会更可靠。

## 使用示例

假设你正在使用 SSR，SSR 的本地代理端口是：

```text
127.0.0.1:20088
```

你希望：

```text
Chrome       -> PROXY  -> SSR 20088 -> ChatGPT
WeChat       -> DIRECT -> 本机直连
BaiduNetdisk -> DIRECT -> 本机直连
```

在 ProxyPilot 中：

1. 确认上游代理显示为 `127.0.0.1:20088`。
2. 把 `Google Chrome` 设置为 `PROXY`。
3. 把 `WeChat` 设置为 `DIRECT`。
4. 把 `Baidu Netdisk` 设置为 `DIRECT`。
5. 点击 `应用规则`。

最终链路应该类似：

```text
Chrome -> ProxyPilot -> SSR 20088
WeChat -> ProxyPilot -> DIRECT
```

## 规则说明

### PROXY

`PROXY` 表示该进程会走 ProxyPilot 的上游代理组。

它不等于“保证能访问外网”。如果 SSR / Clash / 节点本身坏了，`PROXY` 同样会失败。遇到问题时请先看“上游健康检查”。

### DIRECT

`DIRECT` 表示 ProxyPilot 会让 mihomo 对该进程直连，不走上游代理。

如果你同时开了其它全局代理或其它 TUN 工具，它们仍然可能在 ProxyPilot 之外影响网络。为了结果清晰，建议让流量优先进入 ProxyPilot，不要同时开多个全局接管工具。

### REJECT

`REJECT` 表示阻断匹配进程的流量。

## 为什么需要管理员权限

ProxyPilot 使用 mihomo TUN 模式。TUN 能接管那些不遵守 Windows 系统代理的软件，例如部分下载工具、启动器或游戏程序。

在 Windows 上，启用 TUN 通常需要管理员权限。

## 上游代理健康检查

ProxyPilot 能识别本机代理端口，但“端口存在”不代表“代理可用”。

所以 ProxyPilot 会直接通过上游端口检测 Google / YouTube 是否可达。

如果界面显示“上游代理不可用”，请先修复 SSR / Clash / 节点订阅，而不是先怀疑进程规则。

## Chrome 热重载说明

Chrome 会保留连接池、后台 Network Service、QUIC / HTTP3 连接。

修改 Chrome 规则后：

- 新连接通常会按新规则走。
- 旧连接不一定立刻切换。
- 重启 Chrome 是最可靠的验证方式。

ProxyPilot 也提供了“重启选中进程”的辅助按钮。

## 运行时文件

ProxyPilot 会在 exe 所在目录创建运行时文件：

```text
data/settings.json
data/user-rules.json
config/template.yaml
config/config.process-manager.yaml
config/mihomo-generated.yaml
```

这些是用户本机配置，不应该提交到源码仓库。

## 从源码构建

安装 .NET 8 SDK 后执行：

```powershell
dotnet restore .\ProcessProxyManager.slnx
dotnet build .\ProcessProxyManager.slnx -c Release
```

打包 self-contained 版本：

```powershell
.\scripts\package.ps1 -Version 1.0.6
```

## 项目结构

```text
ProcessProxyManager.App/      WPF 桌面界面
ProcessProxyManager.Core/     规则、设置、JSON 存储、进程扫描
ProcessProxyManager.Mihomo/   mihomo 配置、进程管理、API 客户端
ProcessProxyManager.Native/   Windows 代理识别和连接检测
resources/                    内置 mihomo.exe
scripts/                      打包脚本
release/                      可直接下载使用的压缩包
```

## 当前限制

- ProxyPilot 不提供代理节点。
- ProxyPilot 不承诺 100% 无泄漏。
- 有些连接可能是本地连接、局域网连接、系统连接，不能简单等同于代理失败。
- Chrome / Chromium 浏览器改规则后可能需要重启。
- 同时运行多个 TUN / 全局代理工具会让路由变复杂。

## Star 记录

| 日期 | Stars | 备注 |
| --- | ---: | --- |
| 2026-07-09 | 0 | 初始公开版 1.0.6 |

## License

MIT
