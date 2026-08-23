#!/usr/bin/env bash
# Nori Desktop Pet — Linux / macOS 发布脚本
#
# 与 publish.bat 保持同样口径: framework-dependent 发布, 不打包 .NET 运行时。
# 用法:
#   ./publish.sh                  # 按当前系统推断 RID
#   ./publish.sh linux-x64        # 指定单个 RID
#   ./publish.sh osx-arm64 osx-x64
#
# 支持的 RID: linux-x64 linux-arm64 osx-arm64 osx-x64
set -euo pipefail
cd "$(dirname "$0")"

APP_NAME="Nori.Desktop"
APP_VERSION="${NORI_VERSION:-0.1.0}"
BUNDLE_ID="cn.erhio.noriDesktopPet"

# 发布默认 framework-dependent; 手动 CI 可通过 NORI_INCLUDE_RUNTIME=1 打包 .NET Runtime。
SELF_CONTAINED="false"
case "${NORI_INCLUDE_RUNTIME:-0}" in
	1 | true | TRUE | yes) SELF_CONTAINED="true" ;;
esac
SKIP_FRONTEND="${NORI_SKIP_FRONTEND:-0}"
KEEP_SYMBOLS="${NORI_KEEP_SYMBOLS:-0}"
case "$KEEP_SYMBOLS" in
	true | TRUE | yes) KEEP_SYMBOLS="1" ;;
esac

detect_rid() {
	local os arch
	os="$(uname -s)"
	arch="$(uname -m)"
	case "$arch" in
		x86_64 | amd64) arch="x64" ;;
		arm64 | aarch64) arch="arm64" ;;
		*) echo "不支持的 CPU 架构: $arch" >&2; exit 1 ;;
	esac
	case "$os" in
		Linux) echo "linux-$arch" ;;
		Darwin) echo "osx-$arch" ;;
		*) echo "不支持的系统: $os (Windows 请用 publish.bat)" >&2; exit 1 ;;
	esac
}

# macOS 的 .app bundle: Avalonia 需要它才能拿到正常的 Dock/权限行为,
# 麦克风权限也必须在 Info.plist 里声明, 否则 getUserMedia 会被系统直接拒绝。
make_macos_bundle() {
	local rid="$1" publish_dir="$2"
	local app_dir="bin/publish/$rid/Nori.app"
	rm -rf "$app_dir"
	mkdir -p "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources"

	cp -R "$publish_dir/." "$app_dir/Contents/MacOS/"

	cat > "$app_dir/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key><string>Nori</string>
	<key>CFBundleDisplayName</key><string>Nori Desktop Pet</string>
	<key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
	<key>CFBundleVersion</key><string>$APP_VERSION</string>
	<key>CFBundleShortVersionString</key><string>$APP_VERSION</string>
	<key>CFBundlePackageType</key><string>APPL</string>
	<key>CFBundleExecutable</key><string>$APP_NAME</string>
	<key>LSMinimumSystemVersion</key><string>12.0</string>
	<key>NSHighResolutionCapable</key><true/>
	<!-- 语音输入: 前端 MediaRecorder 会触发系统麦克风授权, 缺这条会被直接拒绝 -->
	<key>NSMicrophoneUsageDescription</key>
	<string>Nori 需要使用麦克风来进行语音对话。</string>
</dict>
</plist>
PLIST
	echo "已生成 $app_dir"
}

# Linux: tar.gz + .desktop 模板 (安装路径由用户决定, 因此 Exec 用占位符)
make_linux_package() {
	local rid="$1" publish_dir="$2"
	local out_dir="bin/publish/$rid"

	cat > "$out_dir/nori.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Nori Desktop Pet
Comment=Live2D 桌面陪伴伙伴
# 安装后请把 /opt/nori 换成实际安装路径
Exec=/opt/nori/$APP_NAME
Icon=/opt/nori/nori.png
Terminal=false
Categories=Utility;
StartupWMClass=Nori.Desktop
DESKTOP

	tar -czf "$out_dir/nori-$APP_VERSION-$rid.tar.gz" -C "$publish_dir" .
	echo "已生成 $out_dir/nori-$APP_VERSION-$rid.tar.gz"
}

RIDS=("$@")
if [ ${#RIDS[@]} -eq 0 ]; then
	RIDS=("$(detect_rid)")
fi

if [ "$SKIP_FRONTEND" = "1" ]; then
	echo "[1/2] 跳过前端构建, 使用现有 dist/"
else
	echo "[1/2] 构建前端 (pnpm build)..."
	pnpm build
fi

for rid in "${RIDS[@]}"; do
	case "$rid" in
		linux-x64 | linux-arm64 | osx-arm64 | osx-x64) ;;
		*) echo "不支持的 RID: $rid" >&2; exit 1 ;;
	esac

	publish_dir="bin/publish/$rid/app"
	mode="framework-dependent"
	if [ "$SELF_CONTAINED" = "true" ]; then mode="self-contained"; fi
	echo "[2/2] 发布 $APP_NAME ($rid, $mode)..."
	rm -rf "bin/publish/$rid"
	publish_args=(
		-c Release -r "$rid" --self-contained "$SELF_CONTAINED"
		-p:Version="$APP_VERSION"
		-p:NoriSentryDsnNative="${NORI_SENTRY_DSN_NATIVE:-}"
		-p:NoriSentryRelease="${NORI_SENTRY_RELEASE:-}"
		-p:NoriSentryEnvironment="${NORI_SENTRY_ENVIRONMENT:-production}"
		-p:PublishSingleFile=false -p:PublishReadyToRun=false
	)
	if [ "$KEEP_SYMBOLS" = "1" ]; then
		publish_args+=("-p:DebugType=portable" "-p:DebugSymbols=true")
	else
		publish_args+=("-p:DebugType=None" "-p:DebugSymbols=false")
	fi
	dotnet publish "$APP_NAME/$APP_NAME.csproj" "${publish_args[@]}" -o "$publish_dir"

	if [ "$KEEP_SYMBOLS" != "1" ]; then find "$publish_dir" -name "*.pdb" -delete; fi

	case "$rid" in
		osx-*) make_macos_bundle "$rid" "$publish_dir" ;;
		linux-*) make_linux_package "$rid" "$publish_dir" ;;
	esac

	echo "========================================================"
	echo "发布完成: app/desktop/bin/publish/$rid/"
	echo "========================================================"
done
