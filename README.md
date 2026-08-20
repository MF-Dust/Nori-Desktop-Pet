# Nori Desktop Pet

桌面宠物：.NET 10 + Avalonia 12 宿主，Vue 3 界面由 WebView2 承载。

## 仓库结构

```
app/desktop/          桌宠主程序（唯一前端 + 宿主）
  src/                Vue 3 SPA
  Nori.Core/          纯逻辑（配置 / 资源 / 聊天 / 资源服务）
  Nori.Desktop/       Avalonia 宿主（窗口 / 托盘 / 桥接）
    Bridge/
    Tray/
    Windows/
    Assets/
  Nori.Core.Tests/    xUnit
srv/Nori.Gateway/     资源下载签名网关（阿里云 OSS）
docs/                 设计文档与开发规范
```

三个交付物彼此独立，没有共享构建。

## 桌宠主程序

在 `app/desktop/` 下。必须用 **pnpm**（`pnpm-workspace.yaml` 声明了 `patchedDependencies`）。

```bash
pnpm install
pnpm build                          # vue-tsc --noEmit && vite build
dotnet build
dotnet test

dotnet run --project Nori.Desktop            # 生产：托管 wwwroot 里的 dist
NORI_DEV=1 dotnet run --project Nori.Desktop # 开发：WebView 指向 vite :1420
pnpm dev                                      # 仅 vite；NORI_DEV=1 时需要它在跑
```

## 网关

在 `srv/Nori.Gateway/`。先按 `configs/config.example.yaml` 手工创建 `configs/config.yaml`（含 OSS 凭据，已 gitignore）。

```bash
dotnet build
dotnet run                 # 从工作目录读 configs/config.yaml
../build.bat               # win-x64 + linux-x64 framework-dependent 发布

网关发布默认不打包 .NET Runtime、单文件和 ReadyToRun 数据，便于控制产物体积；目标机器需要安装对应 .NET 10 Runtime。资源清单放在 `srv/Nori.Gateway/configs/assets.json`，格式参考 `assets.example.json`，客户端会在下载后校验文件大小和 SHA-256。桌面端发布脚本同样会清理旧产物并移除 PDB。

桌面端发布:

```bash
publish.bat                  # 前端构建 + win-x64 framework-dependent 发布
```
```

## 约定

改代码前先读 `docs/规范.md`。那是约束，不是建议。
