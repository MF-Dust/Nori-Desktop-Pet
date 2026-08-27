# Nori Plugin Specification 1.0

本文记录 Nori Desktop 当前实现的 NPS 1.0 基础设施与本地插件管理边界。插件是受信任的 .NET 进程内扩展；`AssemblyLoadContext` 只用于依赖隔离和卸载尝试，**不是安全沙箱**。

## 核心合同

- `manifest.json` 是插件身份、版本和入口信息的唯一真源；`INoriPlugin` 不重复声明这些元数据。
- **Contribution** 表示插件向 Nori 提供的内容，例如 `IGameProvider`、`IArcadeCartridge` 和 `IHarnessTool`。
- **Capability** 表示插件希望使用的宿主能力。当前只定义 `ui.webview` 与 `arcade`，并独立记录 `Declared`、`Granted`、`Available`。
- 插件只引用 `Nori.Plugin.Abstractions` 及对应领域 Abstractions，不引用 `Nori.Core`、`Nori.Desktop`、`AppServices`、`IServiceProvider`、Bridge、窗口管理或业务运行时。
- 插件管理只走 Host Vue → `NoriBridge` → Plugins command domain → `PluginManager`。插件自己的 `PluginBridge` 白名单不包含安装、启用、禁用或卸载命令。

## 程序集与依赖

```text
Nori.Plugin.Abstractions
├── Nori.Plugin.Games.Abstractions
├── Nori.Plugin.Arcade.Abstractions
└── Nori.Plugin.Harness.Abstractions

Nori.Plugin.Runtime ──> 四个 Abstractions + Nori.Core（仅宿主内部复用安全文件/ZIP能力）
Nori.Desktop ─────────> Nori.Plugin.Runtime + Nori.Core + Avalonia
```

四个公开合同程序集均为 `net10.0`、Nullable/ImplicitUsings 开启、`LangVersion=latest`、`TreatWarningsAsErrors=true`，不引用 Avalonia、ASP.NET Core、Vue 或宿主程序集。`Nori.Plugin.Runtime` 是宿主实现，不应被插件引用。

## manifest.json

最小示例：

```json
{
  "schemaVersion": 1,
  "id": "io.nori.games",
  "name": "Nori Games",
  "description": "Nori games",
  "version": "1.0.0",
  "authors": [{ "name": "MF-Dust" }],
  "homepage": "https://example.invalid",
  "repository": "https://example.invalid/repo",
  "license": "MIT",
  "apiVersion": "1.0",
  "minHostVersion": "1.0.0",
  "runtime": {
    "kind": "dotnet",
    "assembly": "lib/Nori.Games.dll",
    "entryType": "Nori.Games.Plugin"
  },
  "ui": { "webRoot": "web" },
  "capabilities": ["ui.webview"],
  "optionalCapabilities": [],
  "platforms": ["windows", "linux", "macos"],
  "dependencies": []
}
```

`PluginManifestReader` 在加载程序集前检查：

- `schemaVersion` 必须为整数 `1`；schema、插件 SemVer 和 API `major.minor` 是三个独立概念。
- `id` 必须匹配 `^[a-z0-9]+(\.[a-z0-9_-]+)+$`；`runtime.kind` 当前只允许 `dotnet`。
- `runtime.assembly` 必须是 `lib/` 下的 `.dll`，`entryType` 不接受 assembly-qualified 字符串。
- `ui.webRoot` 必须位于 `web/` 下；列表项不能重复；required/optional capability 不能交叉。
- 依赖格式为 `<id>` + 空格分隔的 `>=`、`>`、`<=`、`<`、`=` 约束，例如 `>=1.0.0 <2.0.0`。
- 重复 JSON 属性（包括大小写变体）、非法路径、未知 schema 和其它解析失败都转换为带稳定 `Code` 的 `PluginException`，不会只泄漏 `JsonException`。

API 兼容规则为：Host major 必须等于插件 major，且 Host minor 不小于插件 minor。因此插件 API 1.2 只接受 Host 1.2/1.5，不接受 1.0/1.1/2.0。

## `.noripack` 与安装目录

`.noripack` 是 ZIP，允许的布局为：

```text
manifest.json
README.md       (可选)
LICENSE         (可选)
icon.png        (可选)
lib/            (托管入口与插件私有依赖)
web/            (公开 Web 资源)
assets/         (公开资源)
locales/        (公开资源)
runtimes/       (插件私有运行时依赖)
```

宿主数据目录：

```text
<data>/plugins/
├── inbox/*.noripack
├── .staging/<temporary>/
└── <pluginId>/
    ├── current.json
    ├── 1.0.0/
    └── 1.1.0/

<data>/plugin-data/
├── runtime/
│   ├── plugin-state.json
│   └── plugin-startup.json
└── <pluginId>/storage.json
```

安装顺序是包预检、同卷 staging、安全解压、manifest/入口/引用复校验、版本目录原子移动、`current.json` 原子替换。旧 current 不会被失败安装覆盖，插件数据不放入版本目录。ZIP Slip、绝对路径、`..`、控制字符、重复规范化路径、符号链接、单文件/总展开大小及压缩比均拒绝。包中携带四个 contract DLL 也拒绝。

设置页的“本地安装”只允许宿主 Avalonia `StorageProvider` 文件选择器产生 `.noripack` 路径。前端没有任意路径安装参数。首次继续安装前会提示插件属于 trusted in-process 扩展；确认只记录在主 WebView 的 localStorage。

**新安装插件默认 `enabled=false`，安装完成后不会自动加载或执行第三方 DLL。** 用户需要在插件设置页显式点击启用。既有安装若没有 `plugin-state.json` 记录，则按兼容语义视为 enabled。

当前没有联网 Marketplace、签名系统或自动更新；活动版本更新只切换 `current.json`，无法安全回收当前 ALC 时由重启完成切换或卸载。

## Runtime 生命周期

```text
manifest/schema/API/platform/dependency/capability validation
  -> Discovered / Loading
  -> collectible PluginLoadContext + AssemblyDependencyResolver
  -> 精确加载 manifest.runtime.entryType
  -> 插件专属 IPluginContext
  -> ActivateAsync
  -> Active / 可枚举 Contribution

显式停用/禁用
  -> Stopping
  -> revoke stopping token / contribution / capability lease
  -> await PluginWindowHost 关闭该插件全部 WebView
  -> DeactivateAsync / cleanup
  -> 清除实例、context 与委托引用
  -> ALC.Unload + 有界 GC 检查
      -> Installed/Disabled
      -> 无法回收时 PendingRestart
```

`PluginStateStore` 固定写入 `plugin-data/runtime/plugin-state.json`，保存用户启用意图以及需要重启后完成的卸载请求。用户禁用不会被 Safe Mode 或启动失败保护覆盖。`plugin-startup.json` 继续只承担启动失败恢复；连续启动失败会触发保护性禁用，显式重新启用时清除该保护后重试。

`EnableAsync` 会校验插件 ID、安装状态、宿主兼容性和依赖，先持久化用户启用意图，再尝试热激活。失败状态保留稳定错误码与脱敏、截断后的用户可读错误，可再次重试。

`DisableAsync` 会持久化 `enabled=false`，撤销 contribution、关闭插件窗口、调用停用清理并尝试回收 ALC。回收失败时保留用户禁用意图并返回 `PendingRestart`。

`UninstallAsync` 只接收经过 manifest ID 规则校验的插件 ID，不接受任意路径。存在活动依赖时拒绝。正常卸载删除 `<plugins>/<pluginId>` 下所有版本和 `current.json`，默认保留 `<plugin-data>/<pluginId>`；用户显式勾选删除数据时才删除该精确目录。若 ALC 或文件仍被占用，运行时持久化 pending-uninstall 与 `deleteData`，下次启动创建任何插件 ALC 前再次尝试完成删除。

重复 `Discover()` 对同一 current 版本的活动插件保持 Active，不会误标 `PendingRestart`。用户 disabled 插件在 discover/startup 阶段绝不创建 ALC。

激活、停用、贡献调用和桥接回调均在宿主边界包装。插件异常记录 `pluginId`、版本、阶段和稳定错误码，不把参数、结果、请求正文、完整路径或 stack trace 暴露给前端 DTO。

## 主设置页插件管理

主设置页的 Extend 分组在 Automation 后提供 Plugins 页面。列表 DTO 已包含详情所需字段，因此没有额外 `plugin_get_details` 命令，也没有本阶段的轮询或 `nori:plugins-changed` 事件。

宿主管理命令固定为：

```text
plugin_list
plugin_install_local
plugin_enable({ id })
plugin_disable({ id })
plugin_uninstall({ id, deleteData })
```

这些命令只允许**当前可见的 main WebView** 调用。`plugin_list` 只做无 ALC 的 refresh/discover；安装取消返回 `{ cancelled: true }`；启用、禁用和卸载优先返回最新稳定 DTO，让前端本地更新列表。

列表 DTO 只暴露 manifest 的公开元数据、固定 state 字符串、用户 enabled 意图、capability status、稳定错误码、脱敏错误文本、requiresRestart 与由 AssetServer 提供的 `iconUrl`。不会序列化安装路径、插件数据路径、ALC、context 或异常对象。

固定 state 字符串为：

```text
installed
loading
active
stopping
disabled
failed
incompatible
pending_restart
```

插件页支持本地安装、显式启用/禁用、失败重试、卸载、可选删除数据、权限状态、错误状态和重启提示。安装、启用、禁用、卸载均不绕过 `PluginManager`。

## Plugin Abstractions

`Nori.Plugin.Abstractions` 的公开边界只有：

- `INoriPlugin`：`ActivateAsync(IPluginContext, CancellationToken)` 与 `DeactivateAsync(CancellationToken)`。
- `PluginDescriptor`、`IPluginContext`、`IPluginLogger`、`IPluginStorage`、`IPluginAssets`。
- `IPluginContribution`、`IContributionRegistry`、幂等的 `IPluginRegistration`。
- `IPluginCapability`、`IPluginCapabilities` 与 `[PluginCapability]`。
- `IWebViewCapability`、`PluginWebViewOptions` 和 `IPluginWebViewWindow`。

Storage 是异步 JSON KV，按插件目录隔离；Assets 只公开 `web/`、`assets/`、`locales/` 和 `icon.png`，每一级都进行 containment 与 symlink/reparse 检查。插件不能看到 SQLite connection 或宿主文件服务。

### Games

`IGameProvider` 是 `IPluginContribution`，包含 `GameDescriptor` 与 `CreateSessionAsync(GameLaunchContext, CancellationToken)`；`IGameSession` 只有 Start/Stop 与 `IAsyncDisposable`。宿主通过 `PluginManager.GetContributions<IGameProvider>()` 枚举，不根据 `chess`、`cakeduel` 等字符串分支。

### Arcade

`IArcadeCartridge` 是纯 reducer：`CreateInitialState()` 与 `ReduceAsync` 返回 `ArcadeReduceResult(State, Result, Events)`。它不负责 WebSocket、world、headVersion、visibleVersion、RFC6902 patch、mount/unmount 或 visibility fence。四个 Nori.Web 游戏尚未移植。

### Harness

Harness 层统一使用 `IHarnessTool`、`IHarnessResourceProvider` 与 `IHarnessEventSource`，不区分 Codex/Claude/OpenCode。工具风险等级为 `Safe`、`Sensitive`、`Destructive`；调用上下文包含 HarnessId、SessionId、TrustLevel 和 GrantedScopes。未来 adapter 负责把本地工具 ID 导出成 `<pluginId>/<toolId>`，资源 URI 使用 `nori-plugin://<pluginId>/...`。

## AssetServer 与 Plugin WebView

现有 loopback `AssetServer` 继续负责随机 prefix、Host allowlist、`/app`、`/nori-assets` 和 `/media`。新增插件资源路径：

```text
/<random-prefix>/plugins/<pluginId>/web/index.html
```

它复用相同的 Host/prefix 校验和精确路径安全检查，不启动第二个 static server。`IPluginAssets.GetUri()` 在生产返回带随机 prefix 的绝对同源 URL，开发模式返回 `/plugins/...`，由 Vite 代理到 14201。`vite.config.ts` 保留 `base: "./"`，新增 `/plugins` 代理。

插件 WebView 位于 `Nori.Desktop/Plugins/`，由独立 `PluginWindowHost` 管理，不加入固定的 `WindowDefinition.All`。窗口标签固定为 `plugin:<pluginId>:<windowId>`；窗口创建时由宿主绑定 descriptor 和 revoke token，页面提交的 `pluginId` 永远不是权限依据。Runtime 只依赖 `PluginRuntimeOptions.ClosePluginWindowsAsync` 回调，Desktop 装配层注入 `PluginWindowHost.CloseAllWindowsForPluginAsync`，因此 Runtime 不引用 Desktop 类型。

`PluginBridge` 不转发到 `NoriBridge`，只保留插件摘要、能力状态、当前窗口信息、关闭自身和 ping 等最小命令，并使用独立的 `window.__noriPlugin.dispatch` 信封。插件管理命令不会转发给插件 WebView。当前没有通用 JS SDK、宿主 Vue 组件注入、Pinia/Router/DOM 访问或任意网络 capability。

## Safe Mode

`--safe-mode` 采用 fail-closed。PluginManager 仍可发现已安装 manifest，列表仍展示 manifest、用户 enabled 意图和 Safe Mode 临时禁用原因，但不会创建 ALC 或执行任何第三方入口。

Safe Mode 允许：

- `plugin_list`
- `plugin_disable`
- `plugin_uninstall`

Safe Mode 拒绝：

- `plugin_install_local`
- `plugin_enable`

安全模式不会覆盖用户在 `plugin-state.json` 中保存的 enabled 意图。退出 Safe Mode 后，用户原本的启用选择仍然存在。

## 当前未实现

- Marketplace、联网下载/安装、签名与供应链验证、自动更新。
- WASM 与 out-of-process 插件沙箱。
- Codex/MCP/HTTP/stdio adapter、完整 Harness 执行审批与工具运行时。
- Arcade WebSocket/world/patch runtime，以及 cakeduel/chess/codenames/pictionary cartridge 移植。
- ARG artifact runtime。
- 完整插件前端 SDK、动态 Vue 注入、插件直接 Avalonia Control。
- 任意 network/filesystem/shell/process/LLM/memory/MCP/automation/pet/chat capability。

`io.nori.games`、Marketplace、签名、Nori.Web runtime 和完整 Arcade/Harness runtime 都不属于当前 Plugin Management Vertical Slice。在这一垂直切片通过构建、测试和审查前，不开始 `io.nori.games` 实现。
