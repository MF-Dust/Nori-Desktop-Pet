# Tauri 2 Window 配置属性参照表

配置位置：

`tauri.conf.json` → `app` → `windows` → `{}`

| 属性 | 类型 | 默认值 | 作用 | 注意事项 |
|---|---|---:|---|---|
| `label` | string | `"main"` | 窗口唯一标识 | 必须唯一；Rust/JS 获取窗口时使用 |
| `title` | string | `"Tauri App"` | 窗口标题 | Windows 标题栏、任务切换等可能使用 |
| `url` | string | 无 | 指定窗口加载的 URL | 通常使用 `devUrl` / `frontendDist` 时不需要 |
| `x` | number | 无 | 窗口初始 X 坐标 | 通常与 `y` 一起使用 |
| `y` | number | 无 | 窗口初始 Y 坐标 | 通常与 `x` 一起使用 |
| `width` | number | 800 | 窗口宽度 | 单位通常为逻辑像素 |
| `height` | number | 600 | 窗口高度 | 单位通常为逻辑像素 |
| `minWidth` | number | 无 | 最小窗口宽度 | `resizable` 开启时更有意义 |
| `minHeight` | number | 无 | 最小窗口高度 | `resizable` 开启时更有意义 |
| `maxWidth` | number | 无 | 最大窗口宽度 | `resizable` 开启时更有意义 |
| `maxHeight` | number | 无 | 最大窗口高度 | `resizable` 开启时更有意义 |
| `resizable` | boolean | `true` | 是否允许用户调整窗口大小 | `false` 后无法拖拽改变大小 |
| `fullscreen` | boolean | `false` | 是否全屏启动 | 与 `maximized` 不同 |
| `maximized` | boolean | `false` | 是否最大化启动 | 不等于全屏 |
| `minimizable` | boolean | `true` | 是否允许最小化 | 对无边框窗口也可能有平台差异 |
| `maximizable` | boolean | `true` | 是否允许最大化 | 对无边框窗口需要注意平台行为 |
| `closable` | boolean | `true` | 是否允许关闭 | 禁止后系统关闭按钮不可用 |
| `focus` | boolean | `true` | 创建窗口后是否自动获取焦点 | 某些桌宠场景可能不希望抢焦点 |
| `center` | boolean | `false` | 是否将窗口居中 | 与 `x` / `y` 的定位需求有关 |
| `visible` | boolean | `true` | 创建时是否显示窗口 | `false` 可用于先创建、初始化后再 `show()` |
| `decorations` | boolean | `true` | 是否显示系统标题栏和边框 | 桌宠、无边框 UI 通常设为 `false` |
| `transparent` | boolean | `false` | 是否允许窗口透明 | 常用于桌宠、悬浮窗 |
| `alwaysOnTop` | boolean | `false` | 是否始终置于其他窗口上方 | 桌宠常用 |
| `alwaysOnBottom` | boolean | `false` | 是否始终位于其他窗口下方 | 与 `alwaysOnTop` 不应同时使用 |
| `skipTaskbar` | boolean | `false` | 是否从任务栏中隐藏窗口 | Windows 下影响任务栏；桌宠常设为 `true` |
| `shadow` | boolean | `true` | 是否显示系统窗口阴影 | 透明/无边框窗口的效果可能依平台不同 |
| `windowClassname` | string | 无 | 设置 Windows 窗口类名 | 主要用于 Windows 平台 |
| `parent` | string | 无 | 设置父窗口 | 值为另一个窗口的 `label` |
| `acceptFirstMouse` | boolean | `false` | 窗口未激活时是否接受第一次鼠标点击 | 主要影响 macOS |
| `tabbingIdentifier` | string | 无 | macOS 窗口 Tab 分组标识 | macOS 专用 |
| `hiddenTitle` | boolean | `false` | 是否隐藏标题文字但保留标题栏 | 主要用于 macOS |
| `titleBarStyle` | string | `"Visible"` | macOS 标题栏样式 | 常见值：`Visible`、`Transparent`、`Overlay` |
| `trafficLightPosition` | object | 无 | macOS 红黄绿按钮的位置 | 与自定义标题栏有关 |
| `fullscreenWindow` | boolean | `false` | 是否让窗口进入 macOS 原生全屏窗口行为 | macOS 专用 |
| `contentProtected` | boolean | `false` | 是否禁止窗口内容被截图/录屏 | 平台支持情况不同 |
| `incognito` | boolean | `false` | 是否使用隐私/无痕 WebView | 平台支持情况不同 |
| `skipTaskbar` | boolean | `false` | 是否隐藏任务栏窗口 | 与应用图标、应用进程不是同一个概念 |
| `devtools` | boolean | `false` | 是否允许开发者工具 | 主要用于开发调试 |
| `dragDropEnabled` | boolean | `true` | 是否允许 WebView 拖放文件 | 关闭后文件拖入行为会变化 |
| `shadow` | boolean | `true` | 是否显示窗口阴影 | 无边框窗口可能需要手动调整视觉效果 |

---

# 与窗口行为最相关的属性

| 需求 | 主要属性 |
|---|---|
| 显示普通窗口 | `visible: true` |
| 启动时隐藏 | `visible: false` |
| 无边框窗口 | `decorations: false` |
| 透明窗口 | `transparent: true` |
| 桌宠窗口 | `transparent: true` + `decorations: false` |
| 始终置顶 | `alwaysOnTop: true` |
| 始终置底 | `alwaysOnBottom: true` |
| 不出现在任务栏 | `skipTaskbar: true` |
| 出现在任务栏 | `skipTaskbar: false` |
| 禁止调整大小 | `resizable: false` |
| 允许调整大小 | `resizable: true` |
| 启动时居中 | `center: true` |
| 指定启动位置 | `x` + `y` |
| 指定窗口尺寸 | `width` + `height` |
| 限制最小尺寸 | `minWidth` + `minHeight` |
| 限制最大尺寸 | `maxWidth` + `maxHeight` |
| 启动时最大化 | `maximized: true` |
| 启动时全屏 | `fullscreen: true` |
| 禁止关闭 | `closable: false` |
| 禁止最小化 | `minimizable: false` |
| 禁止最大化 | `maximizable: false` |
| 启动后自动获得焦点 | `focus: true` |
| 启动后不主动获取焦点 | `focus: false` |
| 防止截图/录屏 | `contentProtected: true` |
| 启用开发者工具 | `devtools: true` |

---

# 几个容易混淆的属性

| 属性 | 控制什么 | 不控制什么 |
|---|---|---|
| `skipTaskbar` | 窗口是否出现在任务栏 | 不决定应用是否运行 |
| `visible` | 窗口是否可见 | 不决定应用是否启动 |
| `decorations` | 系统标题栏/边框 | 不决定任务栏图标 |
| `transparent` | 窗口背景透明能力 | 不决定窗口是否置顶 |
| `alwaysOnTop` | 窗口层级 | 不决定任务栏显示 |
| `focus` | 是否获取输入焦点 | 不决定窗口是否可见 |
| `resizable` | 是否可以调整大小 | 不决定窗口是否可以移动 |
| `width` / `height` | 窗口尺寸 | 不决定网页内容尺寸 |
| `x` / `y` | 窗口位置 | 不决定网页内容位置 |
| `bundle.icon` | 应用/安装包图标 | 不决定窗口是否出现在任务栏 |
| `label` | 窗口的程序内部 ID | 不决定窗口标题 |

---

# 桌宠类窗口重点关注

| 属性 | 桌宠常见设置 | 目的 |
|---|---:|---|
| `decorations` | `false` | 去掉系统标题栏 |
| `transparent` | `true` | 实现透明背景 |
| `alwaysOnTop` | `true` | 保持在其他窗口上方 |
| `resizable` | `false` | 防止用户改变桌宠尺寸 |
| `skipTaskbar` | `true` | 不占用任务栏 |
| `shadow` | `true` | 保留窗口阴影 |
| `visible` | `false` / `true` | 根据启动流程决定 |
| `focus` | `false` | 避免桌宠启动时抢用户焦点 |

---

# 最重要的三个概念

窗口是否存在：

`Window 创建`

↓

窗口是否显示：

`visible`

↓

窗口是否进入任务栏：

`skipTaskbar`

所以：

`visible: false`

≠

`skipTaskbar: true`

两者是完全不同的概念。

一个控制：

**“看不看得到窗口”**

另一个控制：

**“任务栏里有没有这个窗口”**
