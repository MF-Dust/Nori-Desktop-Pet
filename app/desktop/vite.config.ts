import {defineConfig} from "vite"
import vue from "@vitejs/plugin-vue"

// 宿主开发模式下 AssetServer 固定监听的端口 (见 Nori.Core/Assets/AssetServer.cs)
const HOST_ASSET_PORT = 14201

// https://vite.dev/config/
export default defineConfig(async () => ({
	plugins: [vue()],
	// 相对基址: 生产下页面挂在 /<随机前缀>/app/ 里, 用绝对路径引资源会漏掉前缀直接 404.
	// 路由用的是 hash history, 不受相对基址影响.
	base: "./",
	// 桌面端使用现代 WebView(WebView2/系统 webview), 放宽构建目标以支持
	// import.meta.glob 等生成的 top-level await, 避免 es2020 转译失败
	build: {
		target: "esnext"
	},
	// 1. 不要用 vite 的清屏盖掉宿主的编译错误
	clearScreen: false,
	server: {
		// 2. 宿主开发模式固定指向这个端口, 端口被占用直接失败而不是换一个
		port: 1420,
		strictPort: true,
		// 3. 资源请求代理到宿主的回环资源服务, 让开发与生产下前端写法一致 (同源相对路径)
		proxy: {
			"/nori-assets": {
				target: `http://127.0.0.1:${HOST_ASSET_PORT}`,
				changeOrigin: false
			}
		},
		watch: {
			// 4. 不监听 .NET 构建产物
			ignored: ["**/bin/**", "**/obj/**"]
		}
	}
}))
