# Nori Desktop Pet

Nori Desktop Pet 是一个以 Windows 为首要验收平台的 .NET 10 + Avalonia 12 桌面宠物。宿主负责窗口、托盘、SQLite、桥接和原生 Live2D OpenGL；控制台和设置页是 Vue 3 + TypeScript SPA，通过 Avalonia NativeWebView 承载。

> 当前发布口径：只有 Windows framework-dependent ZIP。macOS/Linux 在 CI 中做编译和单元测试，不生成正式发布包。

## 当前范围

- 四个窗口：`first-run`、`init`、`main` 和原生 OpenGL `pet`。
- Windows 使用 WebView2；macOS 使用 WKWebView；Linux 使用 WebKitGTK。
- 桌宠渲染使用仓库内的 `Live2DCSharpSDK` 和 Cubism Core 原生库，不依赖 WebView 的透明区域。
- 模型选择器的产品范围是 `arg-nori` 和 `nori`。模型文件只通过本地资源导入/已有本地资源使用；项目不提供 Live2D 远程下载、CDN、网关或模型更新服务。
- `VoiceService` 在 C# 中选择 HTTP TTS/Whisper 提供商，音频播放和麦克风录音由 `main` WebView 的 WebAudio/MediaRecorder 完成；当前不使用 NAudio。
- API Key 等敏感配置以 `nsec1:<base64(nonce|ciphertext|tag)>` 保存，使用 AES-256-GCM。主密钥交给 Windows DPAPI、macOS Keychain 或 Linux libsecret/文件回退。旧 `enc:dpapi:` 只在 Windows 兼容读取，其他平台要求重新输入该项密钥。

## 运行要求

### Windows 发布包

正式包是 framework-dependent ZIP，不含 .NET Runtime、安装器、自动更新、代码签名或自包含运行时。目标机必须准备：

- Windows x64
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)

ZIP 中包含应用程序集、前端 `wwwroot`、Windows Cubism Core 原生库以及生成的 SBOM/第三方组件清单。发布前必须由维护者显式确认 Avalonia WebView 和 Cubism Core 的分发许可；未知许可不会在清单中被推断或冒充已确认。

### 开发环境

- Windows 10/11、Linux 或 macOS
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Node.js 24 与 pnpm 11
- Linux 编译/运行 NativeWebView 还需要 WebKitGTK/GTK；macOS 使用系统 WebKit

## 构建、测试和运行

命令均在 `app/desktop/` 执行：

```bash
pnpm install
pnpm check:todo       # 一方源码 TODO/FIXME 扫描；vendor/generated/docs backlog 不纳入
pnpm build            # vue-tsc --noEmit && vite build
pnpm test             # Vitest

dotnet build Nori.slnx --configuration Release
dotnet test Nori.slnx --configuration Release --no-build --no-restore
```

生产宿主从同目录的 `dist/`/发布目录提供前端资源：

```bash
dotnet run --project Nori.Desktop
```

开发模式需要先运行 Vite：

```bash
pnpm dev
NORI_DEV=1 dotnet run --project Nori.Desktop
```

### 发布与启动冒烟

Windows 本地发布固定为 framework-dependent：

```cmd
publish.bat
```

CI/维护者可以用安全的隔离 profile 验证发布二进制的两个启动分支。profile 必须是临时目录，程序不会访问真实用户数据，并在写出 readiness JSON 后自动退出：

```powershell
Nori.Desktop.exe --smoke-test first-run --profile "$env:TEMP\nori-smoke-first-run"
Nori.Desktop.exe --smoke-test initialized --profile "$env:TEMP\nori-smoke-initialized"

# 等待 readiness、校验数据目录并绑定退出超时
./scripts/smoke-published.ps1 `
  -BinaryPath ./bin/publish/win-x64/Nori.Desktop.exe `
  -Mode first-run
```

`readiness.json` 表示资源服务、数据库、窗口和托盘装配完成，不代表用户已经完成向导。CI 会同时运行 `first-run` 和 `initialized`。

## 架构边界

```text
Avalonia App
├── WindowManager
│   ├── first-run / init / main: NativeWebView + Vue SPA
│   └── pet: PetWindow + PetGlControl + OpenGL
├── NoriBridge / BridgeCommands
├── AssetServer (127.0.0.1 回环资源与一次性媒体端点)
└── Nori.Core
    ├── SQLite 配置、聊天、记忆与技能
    ├── HTTP LLM/TTS/STT/MCP
    ├── 本地 Live2D 资源导入与安全校验
    └── AES-GCM 密钥保护与平台能力抽象
```

前端业务不直接接触 `window.__nori`，统一经 `services/host` 和 `services/runtime` 访问桥接。窗口通过 `?window=<label>` 选择自己的路由；新增 WebView 窗口需要同步宿主定义、label 联合类型、窗口路由和 Vue Router。

## CI 与发布策略

`.github/workflows/build.yml` 在 push、PR 和手动运行时执行：

1. 一方 TODO/FIXME 扫描；
2. `pnpm build`；
3. `pnpm test`；
4. `dotnet build`；
5. `dotnet test`；
6. Windows FDD 发布、ZIP 必要文件、SHA-256、SBOM、第三方清单和两个启动分支冒烟。

每个 job 内的 .NET 调用按顺序执行，测试使用 `--no-build --no-restore`，避免并发写入 `obj/`。Linux/macOS 只执行编译和单元测试，不参与发布包制作。

`.github/workflows/release.yml` 必须手动运行。它先在标签创建前完成四道门和 Windows/Linux/macOS 编译单测；所有检查通过后才创建/复用指向本次提交的标签，最后才创建 GitHub Release。正式资产只有 `nori-<version>-win-x64-framework-dependent.zip` 及其 checksum、SBOM、第三方 notices 和 release manifest。

## 文档

- [开发任务清单](./docs/开发任务清单.md)：当前已交付能力、明确未承诺范围和验收入口
- [技术地图](./docs/技术.md)：模块边界、启动路径、资源/语音/密钥现实
- [跨平台矩阵](./docs/跨平台.md)：Windows 首要验收与 macOS/Linux 降级
- [Sentry 配置](./docs/Sentry.md)：遥测边界、构建变量和发布前提
- [开发规范](./docs/规范.md)：前端、C#、样式和桥接约定
- [窗口属性参考](./docs/windows.md)

## 贡献

修改前请阅读 [`docs/规范.md`](./docs/规范.md)。提交前至少运行前端构建/测试和 .NET 构建/测试；不要把真实 API Key、用户数据、发布 profile 或临时探针提交到仓库。
