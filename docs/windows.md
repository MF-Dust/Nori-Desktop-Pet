# Avalonia Window 属性参照表

配置位置:

`Nori.Desktop/Windows/WindowDefinition.cs` → `WindowDefinition.All`,
由 `NoriWindow` 构造函数应用到 Avalonia 的 `Window` 上.

## 常用属性

| Avalonia 属性 | 类型 | 默认值 | 作用 | 注意事项 |
|---|---|---:|---|---|
| `Title` | string | `""` | 窗口标题 | 任务栏/任务切换显示 |
| `Width` / `Height` | double | `NaN` | 窗口尺寸 | **单位是 DIP(逻辑像素)**, 与 Tauri 的 `width`/`height` 同义 |
| `MinWidth` / `MinHeight` | double | `0` | 最小尺寸 | `CanResize=true` 时才有意义 |
| `MaxWidth` / `MaxHeight` | double | `∞` | 最大尺寸 | 同上 |
| `Position` | `PixelPoint` | 系统决定 | 窗口屏幕坐标 | **单位是物理像素**, 与 `Width/Height` 的 DIP 不同, 换算要乘 `RenderScaling` |
| `WindowStartupLocation` | enum | `Manual` | 启动位置 | `Manual` / `CenterScreen` / `CenterOwner` |
| `CanResize` | bool | `true` | 是否允许调整大小 | 对应 Tauri `resizable` |
| `WindowState` | enum | `Normal` | 窗口状态 | `Normal` / `Minimized` / `Maximized` / `FullScreen` |
| `WindowDecorations` | enum | `Full` | 系统标题栏与边框 | **Avalonia 12 由 `SystemDecorations` 更名而来**, 同名枚举已移除; 桌宠用 `None` |
| `TransparencyLevelHint` | `IReadOnlyList<WindowTransparencyLevel>` | 空 | 透明/模糊效果 | 按顺序尝试, 实际生效值读 `ActualTransparencyLevel` |
| `Background` | `IBrush?` | 主题色 | 窗口背景 | 逐像素透明需要设 `Brushes.Transparent` |
| `Topmost` | bool | `false` | 是否置顶 | 对应 Tauri `alwaysOnTop` |
| `ShowInTaskbar` | bool | `true` | 是否显示在任务栏 | 对应 Tauri `skipTaskbar` 取反 |
| `Icon` | `WindowIcon?` | 无 | 窗口图标 | 从 `avares://` 资源加载 |
| `SizeToContent` | enum | `Manual` | 按内容自适应尺寸 | 桌宠改用显式 `Width/Height` |

## 透明级别 (`WindowTransparencyLevel`)

| 值 | 效果 | 平台 |
|---|---|---|
| `None` | 不透明 | 全平台 |
| `Transparent` | **逐像素 alpha, 透出桌面** | 全平台; 桌宠用的就是这个 |
| `Blur` | 背景模糊 | 部分平台 |
| `AcrylicBlur` | 亚克力模糊 | Windows 10+ |
| `Mica` | 云母材质 | **仅 Windows 11** |

## 与 Tauri 配置的对应关系

| tauri.conf.json | Avalonia |
|---|---|
| `label` | `WindowDefinition.Label` (不是 Avalonia 概念, 由 `WindowManager` 自行维护) |
| `width` / `height` | `Width` / `Height` |
| `minWidth` / `minHeight` | `MinWidth` / `MinHeight` |
| `resizable` | `CanResize` |
| `center: true` | `WindowStartupLocation = CenterScreen` |
| `visible: false` | 不调用 `Show()` (Avalonia 窗口默认就是未显示的) |
| `decorations: false` | `WindowDecorations = None` |
| `transparent: true` | `Background = Transparent` + `TransparencyLevelHint = [Transparent]` |
| `alwaysOnTop` | `Topmost` |
| `skipTaskbar` | `ShowInTaskbar = false` |
| `shadow: false` | 无直接对应; 无边框窗口默认无系统阴影 |
| `maximizable` / `minimizable` | 无直接对应; 无边框窗口本就没有系统按钮 |

## 桌宠窗口的关键约束

1. **manifest 必须声明 `<supportedOS>`**. WebView 依赖 `NativeControlHost`, 缺这段会直接抛
   `Unable to create child window for native control host`. 见 `Nori.Desktop/app.manifest`.
2. **逐像素透明的完整配方**: 窗口 `WindowDecorations=None` + `Background=Transparent` +
   `TransparencyLevelHint=[Transparent]`; `NativeWebView.Background=Transparent`
   (它会写进 WebView2 的 `DefaultBackgroundColor`); 页面 `html,body{background:transparent}`;
   WebGL 上下文 `alpha:true` 且 `clearColor(0,0,0,0)`. 缺任意一环都会变成不透明底色.
   验证数据见 `技术.md`.
3. **窗口拖动不能靠 CSS**. WebView 吞掉指针事件, `data-tauri-drag-region` 那套失效,
   改为 `invoke("window_start_drag")` 回调宿主, 由 `IPlatformServices.StartWindowDrag`
   发 `WM_NCLBUTTONDOWN` 交给系统.
4. **DIP 与物理像素别混**. `Width/Height` 是 DIP, `Position` 是物理像素,
   前端 `PhysicalSize`/`PhysicalPosition` 传的也是物理像素, 换算统一乘 `RenderScaling`.
