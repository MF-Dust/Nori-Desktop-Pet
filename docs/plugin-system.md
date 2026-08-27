# Nori Plugin Specification 1.0 第一阶段

本文记录 Nori Desktop 当前实现的 NPS 1.0 基础设施边界。插件是受信任的 .NET 进程内扩展；`AssemblyLoadContext` 只用于依赖隔离和卸载尝试，**不是安全沙箱**。

## 核心合同

- `manifest.json` 是插件身份、版本和入口信息的唯一真源；`INoriPlugin` 不重复声明这些元数据。
- **Contribution** 表示插件向 Nori 提供的内容，例如 `IGameProvider`、`IArcadeCartridge` 和 `IHarnessTool`。
- **Capability** 表示插件希望使用的宿主能力。当前只定义 `ui.webview` 与 `arcade`，并独立记录 `Declared`、`Granted`、`Available`。
- 插件只引用 `Nori.Plugin.Abstractions` 及对应领域 Abstractions，不引用 `Nori.Core`、`Nori.Desktop`、`AppServices`、`IServiceProvider`、Bridge、窗口管理或业务运行时。

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

<data>/plugin-data/<pluginId>/storage.json
```

安装顺序是包预检、同卷 staging、安全解压、manifest/入口/引用复校验、版本目录原子移动、`current.json` 原子替换。旧 current 不会被失败安装覆盖，插件数据不放入版本目录。ZIP Slip、绝对路径、`..`、控制字符、重复规范化路径、符号链接、单文件/总展开大小及压缩比均拒绝。包中携带四个 contract DLL 也拒绝。

当前没有联网 Marketplace、签名系统或自动更新；活动版本更新应在后续启动切换，避免覆盖正在使用的目录。

## Runtime 生命周期

```text
manifest/schema/API/platform/dependency/capability validation
  -> Discovered / Loading
  -> collectible PluginLoadContext + AssemblyDependencyResolver
  -> 精确加载 manifest.runtime.entryType
  -> 插件专属 IPluginContext
  -> ActivateAsync
  -> Active / 可枚举 Contribution

停用/退出
  -> Stopping
  -> 取消 IPluginContext.StoppingToken
  -> DeactivateAsync
  -> 撤销全部 IPluginRegistration
  -> 释放 capability lease、关闭插件窗口/session
  -> 清除实例、类型和委托引用
  -> ALC.Unload + 有界 GC 检查
      -> Installed/Disabled，或无法回收时 PendingRestart
```

激活、停用、贡献调用和桥接回调均在宿主边界包装。插件异常记录 `pluginId`、版本、阶段和稳定错误码，不把参数、结果、请求正文或异常敏感路径送入遥测。连续启动失败会记录在 `plugin-data/runtime/plugin-startup.json`，同一插件连续两次失败后自动标记禁用，避免每次启动反复执行坏插件。

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

Harness 层统一使用 `IHarnessTool`、`IHarnessResourceProvider` 与 `IHarnessEventSource`，不区分 Codex/Claude/OpenCode。工具风险等级为 `Safe`、`Sensitive`、`Destructive`；调用上下文包含 HarnessId、SessionId、TrustLevel 和 GrantedScopes。未来 adapter 负责把本地工具 ID导出成 `<pluginId>/<toolId>`，资源 URI 使用 `nori-plugin://<pluginId>/...`。

## AssetServer 与 Plugin WebView

现有 loopback `AssetServer` 继续负责随机 prefix、Host allowlist、`/app`、`/nori-assets` 和 `/media`。新增插件资源路径：

```text
/<random-prefix>/plugins/<pluginId>/web/index.html
```

它复用相同的 Host/prefix 校验和精确路径安全检查，不启动第二个 static server。`IPluginAssets.GetUri()` 在生产返回带随机 prefix 的绝对同源 URL，开发模式返回 `/plugins/...`，由 Vite 代理到 14201。`vite.config.ts` 保留 `base: "./"`，新增 `/plugins` 代理。

插件 WebView 位于 `Nori.Desktop/Plugins/`，由独立 `PluginWindowHost` 管理，不加入固定的 `WindowDefinition.All`。窗口标签固定为 `plugin:<pluginId>:<windowId>`；窗口创建时由宿主绑定 descriptor 和 revoke token，页面提交的 `pluginId` 永远不是权限依据。窗口停用时关闭并释放自己的 `PluginBridge`。

`PluginBridge` 不转发到 `NoriBridge`，只保留插件摘要、能力状态、当前窗口信息、关闭自身和 ping 等最小命令，并使用独立的 `window.__noriPlugin.dispatch` 信封。当前没有通用 JS SDK、宿主 Vue 组件注入、Pinia/Router/DOM 访问或任意网络 capability。

## Safe Mode

`--safe-mode` 是人工命令行排障模式。PluginManager 仍可发现已安装 manifest，但会把第三方插件标记为 `Disabled`，跳过 inbox 安装、ALC 创建和任何 Activate 调用。Storage、日志、诊断和本地手动修复仍由宿主保留。ALC 无法回收时仅标记 `PendingRestart`，不会为了强制卸载而杀死线程或让应用崩溃。

## 当前未实现

- Marketplace、联网安装、签名/供应链验证、WASM 和 out-of-process 沙箱。
- Codex/MCP/HTTP/stdio adapter、完整 Harness 执行审批与工具运行时。
- Arcade WebSocket/world/patch runtime，以及 cakeduel/chess/codenames/pictionary cartridge 移植。
- ARG artifact runtime。
- 插件管理 Vue 页面、完整前端 SDK、动态 Vue 注入、插件直接 Avalonia Control。
- 任意 network/filesystem/shell/process/LLM/memory/MCP/automation/pet/chat capability。

下一步 `io.nori.games` 应先只引用四个公开程序集中的 Abstractions：实现四个 `IGameProvider` 或 Arcade cartridge，使用 `IPluginStorage` 保存版本无关的存档，通过 `IPluginAssets.GetUri()` 读取自己的页面资源；宿主再单独实现游戏 session 生命周期、权限和 Harness adapter，不把游戏逻辑塞进 `BridgeCommands`。
