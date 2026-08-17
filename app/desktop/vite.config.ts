import {defineConfig} from "vite"
import vue from "@vitejs/plugin-vue"

// @ts-expect-error process is a nodejs global
const host = process.env.TAURI_DEV_HOST

// https://vite.dev/config/
export default defineConfig(async () => ({
	plugins: [vue()],
	// Tauri 桌面端使用现代 WebView(WebView2/系统 webview), 放宽构建目标以支持
	// import.meta.glob 等生成的 top-level await, 避免 es2020 转译失败
	build: {
		target: "esnext"
	},
	// Vite options tailored for Tauri development and only applied in `tauri dev` or `tauri build`
	//
	// 1. prevent Vite from obscuring rust errors
	clearScreen: false,
	// 2. tauri expects a fixed port, fail if that port is not available
	server: {
		port: 1420,
		strictPort: true,
		host: host || false,
		hmr: host
			? {
				protocol: "ws",
				host,
				port: 1421
			}
			: undefined,
		watch: {
			// 3. tell Vite to ignore watching `src-tauri`
			ignored: ["**/src-tauri/**"]
		}
	}
}))
