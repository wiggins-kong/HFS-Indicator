# AGENTS.md

给 AI coding agent 的说明。**给人看的用法在 [README.md](README.md)，这里不重复。**

## 这是什么

`HfsIndicator` 是一个 Windows 通知区托盘程序，用来观察和控制本机已装成 Windows 服务的 [HFS](https://github.com/rejetto/hfs)。单进程、无窗口、无安装程序、无自更新。

进程模型（改代码前必须理解）：

- HFS 由 Windows 服务 `HFS`（NSSM 包装 `D:\Programs\hfs\hfs.exe`，工作目录 `D:\Programs\hfs`）托管，开机即起，登录前就在跑。
- 托盘只做三件事：读状态、发服务控制命令、开关自己的开机自启。它**不拥有** HFS 进程。
- 托盘以管理员权限运行：`app.manifest` 声明 `requireAdministrator`；本机 UAC 设置为不提示（`ConsentPromptBehaviorAdmin=0`），所以双击时静默提升。

## 环境与命令

本机：Windows 11 Pro、.NET SDK 10.0.404、.NET Desktop Runtime 10.0.15、PowerShell 7。

```powershell
dotnet build                 # 编译
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
# 产物：bin\Release\net10.0-windows\win-x64\publish\HfsIndicator.exe（约 1.5 MB）
```

没有测试项目。远端是 `github.com/wiggins-kong/HFS-Indicator`（公开），CI 只做发版：推 `v*` 标签触发 `.github/workflows/release.yml` 编译并建 Release，不跑测试。**验收 = 下面「改动后自检」的 9 条，改完必须人工跑一遍**，不能只说"应该没问题"。

## 代码地图

| 文件 | 职责 | 动它的含义 |
| --- | --- | --- |
| `HfsIndicator.csproj` | `net10.0-windows`、WinExe、单文件发布；唯一 NuGet 包 `System.ServiceProcess.ServiceController` | 换目标框架或加依赖 |
| `app.manifest` | `requireAdministrator` + PerMonitorV2 DPI | 改权限模型 |
| `Program.cs` | 单实例互斥、读 `HfsIndicator.json`（只读）、启动 `ApplicationContext` | 改启动流程或配置 |
| `TrayIcon.cs` | `NotifyIcon`、右键菜单、状态机、3 秒轮询、气泡通知、左右键行为 | 改交互 |
| `Assets/seagull.png` | 内嵌的主图标底图（256px、透明底、无状态灯） | 换主图标视觉 |
| `Assets/seagull.ico` | exe 文件图标（由 `seagull.png` 派生的多尺寸 ico） | 换 exe 在资源管理器里的图标 |
| `HfsApi.cs` | 对 `localhost:{port}` 的 HTTP 调用与 JSON 解析 | 改 HFS 接口 |
| `HfsService.cs` | 服务定位、启动/停止/重启、15 秒超时、错误文案 | 改服务控制 |
| `Autostart.cs` | 计划任务 `HfsIndicator`（登录时、最高权限、延迟 10 秒）的创建/删除/状态检查 | 改开机自启机制 |
| `README.md` | 给人看的用法 | 用户可见行为变了就同步 |
| `.github/workflows/release.yml` | 推 `v*` 标签 → 编译 win-x64 单文件 exe → 建 Release | 改发版流程 |

## 硬规则

1. **进程所有权**：HFS 归 Windows 服务 `HFS`。托盘不得启动、托管或 kill `hfs.exe`。
2. **暂停 = `Stop-Service`**。禁止用 `~/api/quit` 做暂停或重启——NSSM 的 `AppExit Restart` 会立刻拉回，那是假暂停。
3. **地址只用 `get_status.urls.http[0]`**。禁止用 `config.yaml` 的 `base_url`（实测已成死地址，见下方接口备忘）。
4. **配置只读**：`HfsIndicator.json` 存在才读，任何情况下不创建、不修复、不删除。不提供设置窗口。
5. **开机自启只由菜单勾选驱动**。启动时不得偷偷创建或删除计划任务。勾选态 = 计划任务 `HfsIndicator` 存在且动作路径等于当前 exe。不能用启动文件夹 `.lnk`：exe 要提权，登录阶段 Windows 会直接丢弃这类启动项（实测 QuickLook 能起、这个托盘不起）。
6. **不自动拉起已停止的服务**。登录时服务是停的就显示灰色，等用户点。
7. **提权只靠 `app.manifest`**。不要写运行时的二次提权（`ProcessStartInfo.Verb = "runas"`）。
8. **托盘图标 = 内嵌 `Assets/seagull.png` + 运行时画的右下角状态灯**，只在 `TrayIcon.cs` 一处可改。exe 文件图标用 `Assets/seagull.ico`（由同一张 png 派生，换图时两个文件一起重生成），它只出现在资源管理器和任务管理器。
9. **无窗口、无日志文件、除 `System.ServiceProcess.ServiceController` 外无第三方依赖**。
10. **所有 HFS 调用走 `localhost:{port}`**，POST 必须带 `x-hfs-anti-csrf: 1`，否则 403。

## HFS 接口备忘（实测于 HFS 3.4.1 / API 13.72）

```text
POST http://localhost:{port}/~/api/{command}
content-type: application/json
x-hfs-anti-csrf: 1
body: {}
```

- 认证：HFS 配置 `localhost_admin: true`，本机来源免登录。**不要在托盘里存任何密码。**
- `get_status` → `version`、`apiVersion`、`http.listening`、`http.port`、`urls.http[]`、`baseUrl`、`started`、`alerts[]`
- `get_connections` → `list[{ip, port, user, agent, path, got, sent, outSpeedKb, started}]`
- `get_connection_stats` → `{connections, ips, outSpeedKb, inSpeedKb, sent_got}`
- 不存在 `get_monitor`（会返回 `invalid api`）。

两个坑：

- `config.yaml` 里的 `base_url` 是手填的，实测已过期（写着 `192.168.13.13`，实际 IP 是 `192.168.3.5`）。只信 `get_status.urls`。
- HFS 会自动更新，上述内部字段随时可能改名。JSON 解析失败时必须降级为"状态不可用"，**不能让托盘崩溃或退出**。

## 改动后自检

1. `dotnet publish` 无警告，产物是单个 exe，且 exe 在资源管理器里显示海鸥图标。
2. 双击 → 无窗口、通知区只多一个图标、进程是提升的。
3. 右键 → 暂停服务：服务 `Stopped`、1478 不再监听、图标变灰、弹一次气泡。
4. 右键 → 启动服务：服务 `Running`、首页 200、图标变绿。
5. 右键 → 重启服务：断约 1–2 秒后恢复，图标不闪红。
6. 勾选开机自启：任务计划程序里出现 `HfsIndicator`（登录时触发、最高权限、延迟 10 秒、动作指向当前 exe），启动文件夹里的旧 `HFS 状态托盘.lnk` 被清掉；然后**重启**，托盘自己出现，全程没有 UAC。
7. 把 exe 挪到别处再运行：自启显示未勾选；再勾一次，任务的动作路径被覆盖成新路径。
8. 服务 `Running` 但 1478 无响应：连续两次探活失败后变红，不是绿色假象。
9. 删掉 `HfsIndicator.json` 后运行：行为不变，且运行结束后目录里仍没有该文件。

## 不要加的东西

安装包、设置窗口、日志文件、端口扫描、自更新、托盘自己的网络监听、除 `System.ServiceProcess.ServiceController` 之外的依赖。

## 已知限制

- 换机器时对方必须把 HFS 装成 **Windows 服务**，否则"暂停/启动"没有对象，只剩状态灯。
- 缺 .NET 10 桌面运行时的提示框由 Windows 宿主弹出，**英文**，且只在启动前出现。
- HFS 内部 API 变更后连接数会消失，绿色/灰色/红色状态不受影响。
- 自启项在 `任务计划程序` 里（任务名 `HfsIndicator`），**不在** Windows「设置 → 启动应用」里；手动删了任务，下次打开托盘会看到勾选没了。
