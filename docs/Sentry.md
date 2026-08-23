# Sentry 遥测与发布配置

Nori 有两个 Sentry 项目：

- Native：.NET/Avalonia 宿主异常与固定操作名事务；
- Web：Vue/WebView 异常、路由性能和主窗口回放。

默认配置的 `telemetry_enabled` 为 `true`，用户可在首次运行或设置中关闭。关闭后 Native SDK 和 Web transport 停止发送，本地日志仍保留。没有注入 DSN 的开发构建不会初始化可联网的 SDK。

## 数据边界

遥测脱敏边界不上传聊天正文、提示词、记忆正文、语音转写、MCP 参数/结果、API Key、Cookie、请求正文或用户身份。事件只保留异常类型、清理后的堆栈和固定操作名；Web 回放只在 `main` 开启，并遮罩文字/输入框、禁止媒体与用户端点数据。

采样策略由 `src/services/telemetry/policy.ts` 管理：错误全量、性能抽样、普通回放低比例、错误回放全量。Web 不向用户配置的 LLM/MCP/资源 URL 传播 Sentry trace header。

## 构建变量

Native 配置在 MSBuild 生成的中间文件中注入，不写入仓库：

```text
NORI_SENTRY_DSN_NATIVE
NORI_SENTRY_RELEASE
NORI_SENTRY_ENVIRONMENT
```

Web 构建变量为：

```text
VITE_SENTRY_DSN_WEB
NORI_SENTRY_RELEASE
NORI_SENTRY_ENVIRONMENT
```

`SENTRY_AUTH_TOKEN` 只用于构建期 source map/发布管理，不能进入 ZIP。DSN 是公开项目标识，不是鉴权令牌。

## GitHub Actions Secrets

当前 `.github/workflows/release.yml` 的正式发布前端门要求：

- `SENTRY_AUTH_TOKEN`
- `SENTRY_ORG`
- `SENTRY_PROJECT_WEB`
- `SENTRY_PROJECT_NATIVE`
- `SENTRY_DSN_WEB`
- `SENTRY_DSN_NATIVE`
- `SENTRY_URL`（SaaS 可省略，默认为 `https://sentry.io`）

Web source map 在四道门的前端构建中上传并从 `dist` 删除；没有成功处理的 `.map` 会阻断发布。Native release 在标签创建后由独立 job 创建/复用并 finalize。当前 Windows 发布使用无 PDB 的正式 ZIP，因此 release workflow 不把符号文件放进用户包。

## 发布顺序与固定口径

release 是手动 workflow，顺序不可颠倒：

```text
许可确认
→ 版本预检
→ 一方 TODO/FIXME 扫描
→ pnpm build / pnpm test
→ dotnet build / dotnet test
→ Windows、Linux、macOS 编译与单测
→ Windows framework-dependent 发布与双分支冒烟
→ 创建/复用指向本次提交的标签
→ finalize Native Sentry
→ 创建 GitHub Release
```

Windows 是正式发布 blocker。release 只生成 `win-x64` framework-dependent ZIP，目标机必须安装 .NET 10 Runtime 和 WebView2；不提供 self-contained、安装器、签名或 macOS/Linux 正式资产。工作流不会再有 `include_runtime` 开关。

发布输入还必须显式为 true：

```text
confirm_avalonia_webview_distribution_license
confirm_cubism_core_distribution_license
```

它们分别映射为 `NORI_AVALONIA_WEBVIEW_DISTRIBUTION_LICENSE_CONFIRMED` 和 `NORI_CUBISM_CORE_DISTRIBUTION_LICENSE_CONFIRMED`，用于阻止维护者在未确认 Avalonia WebView/Cubism Core 分发许可时创建标签或 Release。

## 本地诊断

发布二进制支持：

```powershell
Nori.Desktop.exe --smoke-test first-run --profile "$env:TEMP\nori-smoke-first-run"
Nori.Desktop.exe --smoke-test initialized --profile "$env:TEMP\nori-smoke-initialized"
```

`--profile` 是强制的隔离目录。宿主在数据库、AssetServer、窗口和托盘装配完成后原子写出 `readiness.json`，随后自动退出；`scripts/smoke-published.ps1` 还会校验数据目录没有越界并设置等待/退出超时。该模式不会接触真实用户目录，也不改变普通启动路径。

## 当前产品边界

Sentry 只观察宿主和 Web UI 的诊断事件；它不负责模型下载、CDN、更新服务或用户数据备份。模型范围仍是本地 `arg-nori`/`nori`，语音实现是 C# `VoiceService` + WebAudio/MediaRecorder，不是 NAudio。
