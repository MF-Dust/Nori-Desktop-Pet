# Sentry 遥测配置

Nori 的 Native 宿主与 Vue/WebView 使用两个独立的 Sentry 项目：

- Native 项目接收 .NET/Avalonia 未处理异常与固定操作名性能事务。
- Web 项目接收 Vue/WebView 异常、路由性能事务与全局遮罩的 Session Replay。

默认配置中的 `telemetry_enabled` 为 `true`，用户可以在首次运行向导最后一步或「设置 → 系统与常规设置 → 诊断与隐私」关闭。关闭后 Native SDK 和所有 WebView transport 都立即关闭；本地日志仍然保留。

## 数据边界

Sentry 事件不上传聊天内容、提示词、记忆、语音转写、MCP 入参/结果、API Key、Cookie、请求正文或用户身份。错误事件只保留异常类型和可定位的堆栈，事务名只允许固定操作名/路由名。Web 回放只在主窗口启用，全局遮罩文字和输入框并阻断媒体，同时清理本地路径、资源前缀和查询参数。

采样率固定为：错误 100%、性能追踪 25%、普通回放 5%、错误回放 100%。Web 侧不向用户配置的 LLM/MCP/资源端点传播 Sentry trace header。

没有注入 DSN 的本地/开发构建不初始化 SDK，也不会因为加载 Sentry 包而联网。

## 构建注入

Native DSN、release 和环境在 .NET 发布时通过 MSBuild 属性编译进中间生成文件，不写入仓库：

```text
NORI_SENTRY_DSN_NATIVE
NORI_SENTRY_RELEASE
NORI_SENTRY_ENVIRONMENT
```

Web DSN 使用 Vite 的公开构建变量：

```text
VITE_SENTRY_DSN_WEB
```

DSN 是 Sentry 项目标识，不是鉴权令牌；`SENTRY_AUTH_TOKEN` 只用于构建期上传 source map/符号，不能打进应用。

## GitHub Secrets

`.github/workflows/release.yml` 需要维护者预先配置：

- `SENTRY_AUTH_TOKEN`
- `SENTRY_ORG`
- `SENTRY_PROJECT_WEB`
- `SENTRY_PROJECT_NATIVE`
- `SENTRY_DSN_WEB`
- `SENTRY_DSN_NATIVE`
- `SENTRY_URL`（Sentry SaaS 可留空，自托管实例填写地址）

## 手动发布

现有 `build.yml` 已取消 push/PR 自动触发，只能通过 GitHub Actions 的 **Run workflow** 手动运行。它保留三平台四道门，并提供 `include_runtime` 输入：

- `false`：framework-dependent，默认行为，目标机需要安装 .NET 10 Runtime。
- `true`：self-contained，把 .NET Runtime 一起放入发布目录，包体更大但不要求目标机预装 .NET Runtime。

正式发布使用同样是手动触发的 `release.yml`。标签支持 `v<major>.<minor>.<patch>-<Codename>` 格式，例如 `v1.0.0-Arona`；`tag` 可以填写已有标签，也可以填写希望本次新建的标签，工作流会校验、创建并推送不存在的标签。留空时，工作流会读取最新标签，按 `bump` 输入的 `patch` / `minor` / `major` 自动创建并推送新标签，默认继承最新标签的 Codename，也可以通过 `codename` 输入覆盖，然后继续正式发布。仓库没有历史标签时，首次使用项目当前初始版本（默认 `v0.1.0`）创建标签。它只创建 Sentry release 并上传 Actions artifacts，不创建 GitHub Release。Web 构建只做一次并上传 hidden source map；三平台再复用同一份 `dist` 发布。

两种模式都仍然需要目标系统提供 WebView2/WebKitGTK、Live2D Cubism Core 等现有平台依赖。

## 本地发布脚本

默认行为保持 framework-dependent 且最终删除 PDB：

```bash
./publish.sh
```

维护者或 CI 可以使用：

```bash
NORI_VERSION=0.1.0 \
NORI_INCLUDE_RUNTIME=1 \
NORI_SKIP_FRONTEND=1 \
NORI_SENTRY_DSN_NATIVE="$SENTRY_DSN_NATIVE" \
NORI_SENTRY_RELEASE=nori@0.1.0 \
./publish.sh linux-x64
```

`publish.bat` 支持同名环境变量。`NORI_KEEP_SYMBOLS=1` 会保留 portable PDB，供上传到 Native 项目后再剥离；普通本地发布不需要设置它。
