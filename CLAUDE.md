# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository layout

Three independent deliverables, no shared build:

- `app/desktop/` — the Nori desktop pet. **.NET 10 + Avalonia 12 host** (`Nori.Desktop/`, `Nori.Core/`, C#) + Vue 3 SPA (`src/`, TypeScript/Less) rendered in **WebView2**. This is where nearly all work happens.
- `srv/Nori.Gateway/` — a small ASP.NET Core minimal-API gateway whose only job today is signing Aliyun OSS download URLs for model resources.
- `docs/` — Chinese design docs. `规范.md` is a binding style contract, not advice — read it before touching frontend or C# code. `技术.md` is the module/tech map (and records the pet-window transparency verification), `开发任务清单.md` the roadmap, `windows.md` an Avalonia window-property reference.

`README.md` is empty.

## Commands

Desktop app — run from `app/desktop/`. **Use pnpm**: `pnpm-workspace.yaml` declares `patchedDependencies`, so npm/yarn installs silently produce an unpatched Live2D library.

```bash
pnpm install          # also runs postinstall → scripts/patch-live2d.mjs
pnpm build            # vue-tsc --noEmit && vite build  ← the frontend gate
dotnet build          # builds Nori.Core + Nori.Desktop + tests  ← the backend gate
dotnet test           # xUnit; pure-function coverage
```

Running the app:

```bash
dotnet run --project Nori.Desktop            # production: serves the built dist/ from wwwroot
NORI_DEV=1 dotnet run --project Nori.Desktop # dev: points the WebView at vite on :1420
pnpm dev                                      # vite only; must be running for NORI_DEV=1
```

`规范.md` requires `pnpm build`, `dotnet build` and `dotnet test` to pass before a change is considered done. `tsconfig.json` sets `noUnusedLocals`/`noUnusedParameters`; the C# projects set `TreatWarningsAsErrors`.

Gateway — run from `srv/Nori.Gateway/`:

```bash
dotnet build
dotnet run                 # reads configs/config.yaml relative to the working directory
../build.bat               # self-contained single-file publish for win-x64 + linux-x64
```

`configs/config.yaml` is gitignored (it holds OSS credentials) and must be created by hand from `configs/config.example.yaml`. A missing file now fails at startup with a readable message (the Go version used to panic on first use).

## Architecture

### Four windows, one SPA, one WebView2 each

`Nori.Desktop/Windows/WindowDefinition.cs` declares four windows — `first-run`, `init`, `main`, `pet` — all created hidden, borderless (`WindowDecorations.None`) and transparent. Every window hosts its own `NativeWebView` loading the *same* Vue bundle; which page it shows is decided at runtime:

1. `App.cs` reads `first_run_completed` from SQLite and shows `first-run` or `init`.
2. The host navigates each webview to `…/app/index.html?window=<label>`.
3. Each webview mounts `App.vue`, which calls `navigateToOwnWindow()` — it reads its own label from the query string and `router.replace()`s to the mapped route.
4. The label→route table is `WINDOW_ROUTES` in `src/services/window/index.ts`.

**Adding a window means touching four places**: `WindowDefinition.All`, the `WindowLabel` union, `WINDOW_ROUTES`, and `src/services/router/index.ts`. Miss one and the window renders `/init`.

First-run flow: wizard in `first-run` → `complete_first_run` (C#) marks the DB, closes `first-run`, shows `init`, broadcasts `nori:init-start` → `InitView` downloads the Live2D model → `showWindow("main")`, then closes `init`. Normal launch shows `init` directly. Because `init` starts hidden on the first-run path, `InitView` checks `isVisible()` and otherwise *waits* for `nori:init-start`.

The tray (`Tray/TrayMenu.cs`) is the only always-available entry point: left-click opens `main`, menu toggles `pet`. Closing a window only hides it (`NoriWindow.AllowClose` gates real disposal); `ShutdownMode.OnExplicitShutdown` keeps the process alive.

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
| `resource-download` | `BridgeCommands.EnsureResourceAsync` | `resourceDownload.ts` |
| `nori:init-start` | `complete_first_run` | `InitView.vue` |
| `nori:pet-start` | frontend `emit` → rebroadcast | `PetView.vue` — loads the model |
| `nori:config-changed` | every `set_config` | `PetView.vue` — hot-applies display config |
| `nori:play-motion` | `chat_completion` | `PetView.vue` |
| `nori:window-metrics` | `NoriWindow.PostMetrics` | `services/host/window.ts` cache |

That last one exists for performance: `PetView`'s head tracking runs per animation frame, and three JSON round-trips per frame would be far heavier than Tauri's IPC. The host pushes position/size/scale on change, `host/window.ts` caches it, and only `get_cursor_pos` still does a round-trip. Download progress is throttled to ~10/s for the same reason.

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

### Resource pipeline (spans all three layers)

`createResourceDownload()` → `invoke("ensure_resource")` → `ResourceManager.EnsureAsync` → `GET https://api.elake.top/nori/resource/download_url?type=&name=` → gateway → OSS `DoesObjectExist` + `GeneratePresignedUri` on `<type>/<name>.zip` → streams to `data/temp/<name>.zip.part`, renames on success, safe-extracts to `data/resources/live2d/<name>/`, then re-verifies (Live2D must contain a `.model3.json`).

The `/nori` prefix in that URL comes from an upstream API gateway (`srv/g.sql` registers this service under base path `/nori`); the service itself only registers `/resource/download_url` and `/ping`. Its JSON envelope is `{body, error, message, timestamp}` — **that key order is deliberate**: the Go original marshalled a `map[string]any`, which Go sorts alphabetically. `ApiResponse`'s property order preserves byte-compatibility with old clients.

Progress reaches the UI as `resource-download` events with a step machine: `downloading → download-done → extracting → done`, plus `installed` / `error`. `resourceDownload.ts` queues these and deliberately holds the intermediate steps ~500–700 ms so the text is readable; it is resource-type-agnostic by design.

`ZipExtractor` is hardened — rejects absolute paths, UNC, drive letters, `..`, control chars and symlink entries, and re-canonicalizes each parent against the target. It also strips a single common top-level directory. Don't loosen it.

### Live2D

`live2d-easy-control` is patched **twice**, both applied at `pnpm install`:

- `patches/live2d-easy-control.patch` (pnpm `patchedDependencies`) — sets `img.crossOrigin = "anonymous"` on textures. Now that assets are same-origin this is no longer load-bearing, but it is harmless and still correct.
- `scripts/patch-live2d.mjs` (postinstall, idempotent string replacement on the built bundle) — four behavioral fixes: allow stacked expressions, restore parameters on `stopAllExpressions`, enable `preserveDrawingBuffer`, and make a failed model load *reject* instead of hanging forever.

Both are pinned to exact source snippets. **Upgrading the package will make them no-ops without failing** — the script just logs `跳过`. Re-derive them if you bump the version.

The library appends its `<canvas>` to `document.body` and never removes it, so `createLive2D()` owns the DOM lifecycle and `stage.ts` positions that body-level canvas with `position: fixed`. The pet window sizes *itself* to the model (`applyWindowSize`, center-preserving). Head tracking calls `get_cursor_pos` because a webview can't observe the cursor outside its own window.

### Gateway

Minimal API in `Program.cs`; OSS logic in `Services/OssService.cs`; middleware (CORS → RequestID → request logging) in `Middleware/GatewayMiddleware.cs`, composed in that order to match the Go original. Config is YAML with kebab-case keys, unchanged from the Go version so existing deployments keep working. Logging is Serilog + rolling file, replacing zap + lumberjack.

The Go original's hand-rolled `:param` router and its `bodyParser`/`getQuery`/`fetchURL`/`createFolder` utilities were **not** ported — they had no consumers and ASP.NET Core routing covers the need.

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
