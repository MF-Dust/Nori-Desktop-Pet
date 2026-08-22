# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository layout

Two independent deliverables, no shared build:

- `app/desktop/` — the Nori desktop pet. **.NET 10 + Avalonia 12 host** (`Nori.Desktop/`, `Nori.Core/`, C#) + Vue 3 SPA (`src/`, TypeScript/Less) rendered in **WebView2**. This is where nearly all work happens.
- `docs/` — Chinese design docs. `规范.md` is a binding style contract, not advice — read it before touching frontend or C# code. `技术.md` is the module/tech map (and records the pet-window transparency verification), `开发任务清单.md` the roadmap, `windows.md` an Avalonia window-property reference.

`README.md` is empty.

## Commands

Desktop app — run from `app/desktop/`. **Use pnpm**: the project is managed with pnpm, and `node_modules` layout assumptions in scripts assume it.

```bash
pnpm install          # 安装前端依赖
pnpm build            # vue-tsc --noEmit && vite build  ← the frontend gate
pnpm test             # vitest run  ← 前端纯函数/服务回归
dotnet build          # builds Nori.Core + Nori.Desktop + tests  ← the backend gate
dotnet test           # xUnit; pure-function coverage
./publish.bat         # framework-dependent publish (no bundled runtime) for win-x64
```

Running the app:

```bash
dotnet run --project Nori.Desktop            # production: serves the built dist/ from wwwroot
NORI_DEV=1 dotnet run --project Nori.Desktop # dev: points the WebView at vite on :1420
pnpm dev                                      # vite only; must be running for NORI_DEV=1
```

`规范.md` requires `pnpm build`, `dotnet build` and `dotnet test` to pass before a change is considered done. `tsconfig.json` sets `noUnusedLocals`/`noUnusedParameters`; the C# projects set `TreatWarningsAsErrors`.

## Architecture

### Five windows, three WebView2 + native OpenGL + native settings

`Nori.Desktop/Windows/WindowDefinition.cs` declares five windows — `first-run`, `init`, `main`, `settings`, `pet`.
- Three windows (`first-run`, `init`, `main`) are `NoriWindow` hosting `NativeWebView` and loading the Vue bundle; they remain hidden, borderless and transparent.
- The settings window (`settings`) is a standard native Avalonia `SettingsWindow`, with the Nori deep-sea Fluent visual language and direct typed runtime operations. It is not a Vue route or WebView label.
- The desktop pet (`pet`) is a native Avalonia `PetWindow` hosting `PetGlControl` (OpenGL via `Live2DCSharpSDK`), bypassing WebView2 airspace and window-region clipping issues completely.

1. `App.cs` reads `first_run_completed` from SQLite and shows `first-run` or `init`.
2. The host navigates each webview to `…/app/index.html?window=<label>`.
3. Each webview mounts `App.vue`, which calls `navigateToOwnWindow()` — it reads its own label from the query string and `router.replace()`s to the mapped route.
4. The label→route table is `WINDOW_ROUTES` in `src/services/window/index.ts`.

**Adding a webview window means touching four places**: `WindowDefinition.All`, the `WindowLabel` union, `WINDOW_ROUTES`, and `src/services/router/index.ts`.

First-run flow: wizard in `first-run` → `complete_first_run` (C#) marks the DB, closes `first-run`, shows `init`, broadcasts `nori:init-start` → `InitView` checks the local Live2D model → `showWindow("main")`, then closes `init`. Normal launch shows `init` directly. Because `init` starts hidden on the first-run path, `InitView` checks `isVisible()` and otherwise *waits* for `nori:init-start`.

The tray (`Tray/TrayMenu.cs`) is the only always-available entry point: left-click opens `main`, menu opens `settings` or toggles `pet`. Closing a window only hides it (`NoriWindow.AllowClose` / `SettingsWindow.AllowClose` / `PetWindow.AllowClose` gates real disposal); `ShutdownMode.OnExplicitShutdown` keeps the process alive.

### The bridge (replaces Tauri IPC)

`NativeWebView` only offers JS→host `invokeCSharpAction(string)` and host→JS `InvokeScript`. On top of that:

- **Bootstrap** — an inline `<script>` in `index.html`, before the module script, defines `window.__nori` (`invoke` / `emit` / `listen` / `dispatch`, plus `label` and `assetBase`). It must stay synchronous and first.
- **Frontend API** — `src/services/host/` (`invoke.ts`, `event.ts`, `window.ts`, `shell.ts`). **Never touch `window.__nori` directly from components.**
- **Host side** — `Bridge/NoriBridge.cs` does dispatch/correlation, `Bridge/BridgeCommands.cs` holds the handlers, `Bridge/AppServices.cs` is the service container.

Envelopes are double-encoded: the host serializes the JSON envelope, then serializes *that string* into the `InvokeScript` call, and JS `JSON.parse`s it back. This is deliberate — it makes escaping bugs impossible.

**Every command must be registered in `BridgeCommands.InvokeAsync`'s switch** — an unregistered command compiles fine and fails only at runtime with `未知的命令`. Commands throw on failure; the message is user-facing Chinese text and becomes the frontend's rejection.

Privileged commands allowlist their caller by window label — `complete_first_run` rejects anything but a visible `first-run` webview. Follow that pattern for anything state-changing.

Host→frontend events:

| Event | Emitted by | Consumed by |
|---|---|---|
| `nori:init-start` | `complete_first_run` | `InitView.vue` |
| `nori:config-changed` | every `set_config` | WebViews — hot-applies display config |
| `nori:play-motion` | `chat_completion` | WebViews |
| `nori:window-metrics` | `NoriWindow.PostMetrics` | `services/host/window.ts` cache |

Blocking work (HTTP, zip extraction, SQLite) must stay off the UI thread; anything touching windows or `InvokeScript` must go through `Dispatcher.UIThread`.

### Serving the frontend and assets

There is no custom URI scheme any more — Avalonia's `WebResourceRequested` is read-only and cannot return a response. Instead `Nori.Core/Assets/AssetServer.cs` runs a **Kestrel server bound to `IPAddress.Loopback`** with a per-process random hex path prefix and a `Host`-header check. It mounts:

- `/{secret}/app/*` → the built Vue bundle (`wwwroot`, copied from `dist/`)
- `/{secret}/nori-assets/*` → `%APPDATA%/cn.erhio.noriDesktopPet/data/resources`

App and assets are **same-origin**, so `assetUrl()` is a relative path and there is no CORS to configure. `vite.config.ts` sets `base: "./"` — with an absolute base the built `/assets/…` URLs skip the secret prefix and 404. In dev, `AssetServer` uses fixed port 14201 with no prefix and vite proxies `/nori-assets` to it, so frontend code is identical in both modes.

`AssetPath.cs` is a faithful port of the old `asset.rs`: percent-decoding, absolute/UNC/drive-letter/`..` rejection, canonicalized containment checks, symlink-escape checks, the MIME table, and `PathCandidates`.

**`PathCandidates` only removes path segments, never adds them.** It fixes requests that are one level too deep (a `model3.json` referencing `subdir/tex.png`), *not* zips with an extra nested top-level folder — the old CLAUDE.md claimed the latter and was wrong. The nested-zip case is handled at extraction time instead, by `ZipExtractor.FindCommonTopDirectory`.

### Config: SQLite key/value with inferred types

`nori.db` lives in `%APPDATA%/cn.erhio.noriDesktopPet/data/` with two tables: `config(key TEXT PRIMARY KEY, value TEXT)` and `chat_messages`. Everything is stored as TEXT. **The path must match Tauri's `app_data_dir()` exactly** or existing users lose their data. Note `AppPaths` special-cases macOS: .NET's `SpecialFolder.ApplicationData` is `~/.config` there, while Tauri used `~/Library/Application Support`.

The trap: `ConfigValue.FromStorage` **re-infers the type on read**. `"1"`/`"true"` → Boolean, digit strings → Integer, `{…}`/`[…]` → Json, everything else → String. A config you wrote as a string comes back as a number if it happens to look like one, so `invoke<string | null>("get_config", …)` is a lie for numeric-looking values — `parseNumber()` in `services/live2d/config.ts` exists for exactly this. `"1.25"` stays a String (i64 parse fails, not a JSON container) — `Nori.Core.Tests` pins all of this.

`set_config` broadcasts `nori:config-changed` app-wide, which is how the pet window live-updates. Schema evolution goes through `config_schema_version` + `MigrateSchema()`; a DB newer than the binary is rejected outright.

Live2D display settings are stored **per model** as `<base>_<modelId>` (e.g. `l2d_scale_arg-nori`) with fallback to the legacy global key — see `l2dModelKey()` / `readModelConfig()`. Keep both lookups when adding keys.

### Resource management (local only)

Models are local resources under `data/resources/live2d/<name>/`; there is no remote download or gateway. The frontend calls `check_resource` to test install state and `import_local_resource` to add models from a local ZIP or folder. `ResourceManager` in `Nori.Core/Resources/` covers check/list/delete/import, and Live2D resources count as installed only when they contain a `.model3.json`.

`ZipExtractor` is hardened — rejects absolute paths, UNC, drive letters, `..`, control chars and symlink entries, and re-canonicalizes each parent against the target. It also strips a single common top-level directory. Don't loosen it.

### Live2D: Native OpenGL Desk Pet + PixiJS Setting Preview

Desktop Pet rendering is implemented natively in Avalonia (`PetWindow.cs` + `PetGlControl.cs`) using `Live2DCSharpSDK` and Cubism Native Core:
- Direct OpenGL ES 2.0 rendering in a transparent Avalonia window (`OpenGlControlBase`).
- High-quality 2048x2048 clipping mask buffer, 16x anisotropic filtering, and high precision mask enabled.
- Alpha mask sampling (~10Hz) provides non-blocking Win32 `WM_NCHITTEST` pixel-accurate transparency pass-through without window region clipping artifacts.
- 1:1 C# behavioral pipeline (`AutoBlink`, `EyeFocus`, `IdleDisable`, `BeatSync`, `LipSync`, `ExpressionStore`, `ExpressionBehavior`).
- Native mouse drag (4px threshold + position persistence), tap actions/expressions, global cursor tracking, and deep sea glow themed context menu.
- Settings page preview retains PixiJS + `pixi-live2d-display` with texture mipmapping (`baseTexture.mipmap = 1`), 2048 mask buffer, and `devicePixelRatio` DPI super-sampling.

### Local model management

Model management lives in `src/components/settings/ModelManagement.vue`: import local Live2D ZIP/folder, enable an installed model, adjust per-model display settings, and preview. First-run `ModelSelect.vue` only records the chosen model id; the app never downloads it.

## Conventions (from `docs/规范.md` — follow these, they're enforced by review)

- **Tabs**, not spaces, in `.ts` / `.vue` / `.cs` / `.less`. Double quotes. LF.
- Comments, doc comments, log messages and user-facing Chinese strings stay **in Chinese** — match the surrounding files.
- Frontend naming is unusual: **local constants are `UPPER_SNAKE`** (including `const ROUTER = useRouter()`), local variables `camelCase`, exported functions/types `PascalCase`. **C# does not follow this** — it uses normal .NET conventions (`PascalCase` members, `_camelCase` private fields). Config keys and bridge commands stay `snake_case`, verb-first.
- Vue: `<script setup lang="ts">` only, PascalCase filenames, pages in `views/`, pieces in `components/`. Prefer `ref`/`computed`; avoid `reactive` for whole objects.
- Styles: `<style scoped lang="less">`. **All lengths in `rem`** — the root font size is `62.5%` in `theme.less`, so `1rem = 10px`. Colors/radii/shadows come from the CSS variables in `theme.less`.
- i18n text needs **three** edits: `locales/zh-CN.ts`, `locales/en-US.ts`, and the typed accessor tree in `useLanguages.ts`. In components always wrap it as `computed(() => useLanguages().views.xxx)` so it re-renders on locale change.
- Icons go in the `icon` object in `services/icon/index.ts` (24×24 viewBox) and render via `<Icon name="…"/>`.
- Debounce config writes (~400 ms), and give **each field its own timer**; a shared timer silently drops the earlier field's value.
- `App.cs` is assembly only — new logic gets a new module. Every bridge command needs a `///` doc comment showing the frontend `invoke(...)` call.
- Be conservative about new dependencies. Remove debug `console.log` before committing; keep `console.error` on meaningful failure paths. Don't leave scratch files in the repo.

## Known traps

- **`NativeControlHost` needs a manifest.** `Nori.Desktop/app.manifest` must keep its `<compatibility>` / `<supportedOS>` list, or the WebView throws `Unable to create child window for native control host` at startup.
- **Avalonia 12 renamed `SystemDecorations` to `WindowDecorations`** and removed the old enum; the property survives only as `[Obsolete]`.
- **Kestrel rejects `ListenLocalhost(0)`.** Dynamic ports must use `Listen(IPAddress.Loopback, 0)`.
- **`vite.config.ts` must keep `base: "./"`.** An absolute base emits `/assets/…`, which bypasses the AssetServer's secret prefix and 404s — the app loads a blank window with no error.
- **`LibraryImport` requires `AllowUnsafeBlocks`.** `Nori.Core` deliberately uses classic `DllImport` for its three P/Invokes to avoid enabling unsafe code project-wide.
- Window dragging cannot use CSS. `data-tauri-drag-region` is gone; `TitleBar.vue` calls `window_start_drag`.
- `IPlatformServices` throws `PlatformNotSupportedException` off Windows rather than silently degrading — cursor tracking and window dragging are the two Windows-only behaviours.
